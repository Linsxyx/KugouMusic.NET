#![allow(non_snake_case)]

use std::ffi::c_void;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::ptr;
use std::slice;
use std::sync::{Arc, Condvar, Mutex};

use windows::Foundation::{TimeSpan, TypedEventHandler};
use windows::Media::{
    MediaPlaybackStatus, MediaPlaybackType, SystemMediaTransportControls,
    SystemMediaTransportControlsButton, SystemMediaTransportControlsButtonPressedEventArgs,
    SystemMediaTransportControlsTimelineProperties,
};
use windows::Storage::StorageFile;
use windows::Storage::Streams::RandomAccessStreamReference;
use windows::Win32::Foundation::HWND;
use windows::Win32::System::WinRT::{
    ISystemMediaTransportControlsInterop, RO_INIT_MULTITHREADED, RO_INIT_SINGLETHREADED,
    RoInitialize, RoUninitialize,
};
use windows::core::{HRESULT, HSTRING, factory};

const ABI_VERSION: u32 = 1;
const S_OK: i32 = 0;
const E_INVALIDARG: i32 = 0x80070057u32 as i32;
const E_UNEXPECTED: i32 = 0x8000FFFFu32 as i32;

const BUTTON_PLAY: u32 = 1;
const BUTTON_PAUSE: u32 = 2;
const BUTTON_PREVIOUS: u32 = 3;
const BUTTON_NEXT: u32 = 4;

type KgSmtcButtonCallback = unsafe extern "system" fn(button: u32, user_data: *mut c_void);

pub struct KgSmtcSession {
    controls: SystemMediaTransportControls,
    button_token: i64,
    callback_state: Arc<CallbackState>,
    _apartment: RoApartment,
}

struct RoApartment;

impl RoApartment {
    fn initialize_single_threaded() -> windows::core::Result<Self> {
        unsafe {
            RoInitialize(RO_INIT_SINGLETHREADED)?;
        }
        Ok(Self)
    }

    fn initialize_multithreaded() -> windows::core::Result<Self> {
        unsafe {
            RoInitialize(RO_INIT_MULTITHREADED)?;
        }
        Ok(Self)
    }
}

impl Drop for RoApartment {
    fn drop(&mut self) {
        unsafe {
            RoUninitialize();
        }
    }
}

impl Drop for KgSmtcSession {
    fn drop(&mut self) {
        self.callback_state.begin_close();
        let _ = self.controls.RemoveButtonPressed(self.button_token);
        self.callback_state.wait_for_callbacks();
        let _ = self
            .controls
            .SetPlaybackStatus(MediaPlaybackStatus::Stopped);
        let _ = self.controls.SetIsEnabled(false);
    }
}

struct CallbackState {
    callback: Option<KgSmtcButtonCallback>,
    user_data: usize,
    activity: Mutex<CallbackActivity>,
    callbacks_drained: Condvar,
}

struct CallbackActivity {
    closing: bool,
    active_callbacks: usize,
}

impl CallbackState {
    fn new(callback: Option<KgSmtcButtonCallback>, user_data: *mut c_void) -> Self {
        Self {
            callback,
            user_data: user_data as usize,
            activity: Mutex::new(CallbackActivity {
                closing: false,
                active_callbacks: 0,
            }),
            callbacks_drained: Condvar::new(),
        }
    }

    fn invoke(&self, button: u32) {
        let Some(callback) = self.callback else {
            return;
        };
        let Some(_active_callback) = self.try_begin_callback() else {
            return;
        };

        unsafe {
            callback(button, self.user_data as *mut c_void);
        }
    }

    fn try_begin_callback(&self) -> Option<ActiveCallback<'_>> {
        let mut activity = self
            .activity
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        if activity.closing {
            return None;
        }

        activity.active_callbacks += 1;
        Some(ActiveCallback {
            callback_state: self,
        })
    }

    fn begin_close(&self) {
        let mut activity = self
            .activity
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        activity.closing = true;
    }

    fn wait_for_callbacks(&self) {
        let mut activity = self
            .activity
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        while activity.active_callbacks != 0 {
            activity = self
                .callbacks_drained
                .wait(activity)
                .unwrap_or_else(|poisoned| poisoned.into_inner());
        }
    }
}

struct ActiveCallback<'a> {
    callback_state: &'a CallbackState,
}

impl Drop for ActiveCallback<'_> {
    fn drop(&mut self) {
        let mut activity = self
            .callback_state
            .activity
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        activity.active_callbacks -= 1;
        if activity.active_callbacks == 0 {
            self.callback_state.callbacks_drained.notify_all();
        }
    }
}

fn error_code(error: windows::core::Error) -> i32 {
    error.code().0
}

fn ffi_status<F>(operation: F) -> i32
where
    F: FnOnce() -> windows::core::Result<()>,
{
    match catch_unwind(AssertUnwindSafe(operation)) {
        Ok(Ok(())) => S_OK,
        Ok(Err(error)) => error_code(error),
        Err(_) => E_UNEXPECTED,
    }
}

unsafe fn utf16_string(value: *const u16, length: usize) -> Result<String, HRESULT> {
    if value.is_null() {
        return if length == 0 {
            Ok(String::new())
        } else {
            Err(HRESULT(E_INVALIDARG))
        };
    }

    Ok(String::from_utf16_lossy(unsafe {
        slice::from_raw_parts(value, length)
    }))
}

fn with_session<F>(session: *mut KgSmtcSession, operation: F) -> windows::core::Result<()>
where
    F: FnOnce(&mut KgSmtcSession) -> windows::core::Result<()>,
{
    let session = unsafe { session.as_mut() }
        .ok_or_else(|| windows::core::Error::from(HRESULT(E_INVALIDARG)))?;
    operation(session)
}

#[unsafe(no_mangle)]
pub extern "system" fn KgWinRt_GetAbiVersion() -> u32 {
    ABI_VERSION
}

#[unsafe(no_mangle)]
/// # Safety
///
/// `hwnd` must be a valid top-level window handle. `session_out` must point to
/// writable memory for one session pointer, and the callback/user-data pair
/// must remain valid until the session is destroyed.
pub unsafe extern "system" fn KgSmtc_Create(
    hwnd: *mut c_void,
    callback: Option<KgSmtcButtonCallback>,
    user_data: *mut c_void,
    session_out: *mut *mut KgSmtcSession,
) -> i32 {
    ffi_status(|| {
        if hwnd.is_null() || session_out.is_null() {
            return Err(HRESULT(E_INVALIDARG).into());
        }

        unsafe {
            *session_out = ptr::null_mut();
        }

        let apartment = RoApartment::initialize_single_threaded()?;
        let interop =
            factory::<SystemMediaTransportControls, ISystemMediaTransportControlsInterop>()?;
        let controls: SystemMediaTransportControls = unsafe { interop.GetForWindow(HWND(hwnd))? };

        controls.SetIsEnabled(true)?;
        controls.SetIsPlayEnabled(true)?;
        controls.SetIsPauseEnabled(true)?;
        controls.SetIsPreviousEnabled(true)?;
        controls.SetIsNextEnabled(true)?;
        controls.SetIsStopEnabled(false)?;
        controls.SetPlaybackStatus(MediaPlaybackStatus::Stopped)?;

        let callback_state = Arc::new(CallbackState::new(callback, user_data));
        let handler_callback_state = Arc::clone(&callback_state);
        let handler = TypedEventHandler::<
            SystemMediaTransportControls,
            SystemMediaTransportControlsButtonPressedEventArgs,
        >::new(move |_, args| {
            let button = match args.ok()?.Button()? {
                SystemMediaTransportControlsButton::Play => BUTTON_PLAY,
                SystemMediaTransportControlsButton::Pause => BUTTON_PAUSE,
                SystemMediaTransportControlsButton::Previous => BUTTON_PREVIOUS,
                SystemMediaTransportControlsButton::Next => BUTTON_NEXT,
                _ => return Ok(()),
            };

            handler_callback_state.invoke(button);
            Ok(())
        });
        let button_token = controls.ButtonPressed(&handler)?;

        let session = Box::new(KgSmtcSession {
            controls,
            button_token,
            callback_state,
            _apartment: apartment,
        });
        unsafe {
            *session_out = Box::into_raw(session);
        }
        Ok(())
    })
}

#[unsafe(no_mangle)]
/// # Safety
///
/// `session` must be a live handle returned by `KgSmtc_Create`. Every UTF-16
/// pointer must be valid for its accompanying element count for this call.
pub unsafe extern "system" fn KgSmtc_UpdateMetadata(
    session: *mut KgSmtcSession,
    title: *const u16,
    title_length: usize,
    artist: *const u16,
    artist_length: usize,
    artwork_path: *const u16,
    artwork_path_length: usize,
) -> i32 {
    ffi_status(|| {
        let _apartment = RoApartment::initialize_multithreaded()?;
        with_session(session, |session| {
            let title =
                unsafe { utf16_string(title, title_length) }.map_err(windows::core::Error::from)?;
            let artist = unsafe { utf16_string(artist, artist_length) }
                .map_err(windows::core::Error::from)?;
            let artwork_path = unsafe { utf16_string(artwork_path, artwork_path_length) }
                .map_err(windows::core::Error::from)?;

            let updater = session.controls.DisplayUpdater()?;
            updater.SetType(MediaPlaybackType::Music)?;
            let music = updater.MusicProperties()?;
            music.SetTitle(&HSTRING::from(title))?;
            music.SetArtist(&HSTRING::from(artist))?;

            if artwork_path.is_empty() {
                updater.SetThumbnail(None)?;
            } else {
                let file =
                    StorageFile::GetFileFromPathAsync(&HSTRING::from(artwork_path))?.join()?;
                let thumbnail = RandomAccessStreamReference::CreateFromFile(&file)?;
                updater.SetThumbnail(&thumbnail)?;
            }

            updater.Update()
        })
    })
}

#[unsafe(no_mangle)]
/// # Safety
///
/// `session` must be a live handle returned by `KgSmtc_Create`.
pub unsafe extern "system" fn KgSmtc_UpdatePlaybackState(
    session: *mut KgSmtcSession,
    is_playing: i32,
) -> i32 {
    ffi_status(|| {
        with_session(session, |session| {
            session.controls.SetPlaybackStatus(if is_playing != 0 {
                MediaPlaybackStatus::Playing
            } else {
                MediaPlaybackStatus::Paused
            })
        })
    })
}

#[unsafe(no_mangle)]
/// # Safety
///
/// `session` must be a live handle returned by `KgSmtc_Create`.
pub unsafe extern "system" fn KgSmtc_UpdateTimeline(
    session: *mut KgSmtcSession,
    position_milliseconds: i64,
    duration_milliseconds: i64,
) -> i32 {
    ffi_status(|| {
        with_session(session, |session| {
            let duration = duration_milliseconds.max(0);
            let position = position_milliseconds.clamp(0, duration.max(position_milliseconds));
            let duration_ticks = duration.saturating_mul(10_000);
            let position_ticks = position.saturating_mul(10_000);

            let timeline = SystemMediaTransportControlsTimelineProperties::new()?;
            timeline.SetStartTime(TimeSpan { Duration: 0 })?;
            timeline.SetMinSeekTime(TimeSpan { Duration: 0 })?;
            timeline.SetPosition(TimeSpan {
                Duration: position_ticks,
            })?;
            timeline.SetEndTime(TimeSpan {
                Duration: duration_ticks,
            })?;
            timeline.SetMaxSeekTime(TimeSpan {
                Duration: duration_ticks,
            })?;
            session.controls.UpdateTimelineProperties(&timeline)
        })
    })
}

#[unsafe(no_mangle)]
/// # Safety
///
/// `session` must be null or a live handle returned by `KgSmtc_Create`, and it
/// must not be used or destroyed again after this call.
pub unsafe extern "system" fn KgSmtc_Destroy(session: *mut KgSmtcSession) -> i32 {
    ffi_status(|| {
        if session.is_null() {
            return Ok(());
        }

        unsafe {
            drop(Box::from_raw(session));
        }
        Ok(())
    })
}
