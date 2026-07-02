#![allow(non_snake_case)]

use std::ffi::c_void;
use std::mem::zeroed;

use windows::core::{w, HRESULT, PCWSTR, Result as WinResult};
use windows::Win32::Foundation::{
    BOOL, GetLastError, HANDLE, HWND, LPARAM, LRESULT, SetLastError, WIN32_ERROR, WPARAM,
};
use windows::Win32::System::Com::{
    CLSCTX_INPROC_SERVER, COINIT_APARTMENTTHREADED, CoCreateInstance, CoInitializeEx, CoUninitialize,
};
use windows::Win32::UI::Shell::{
    ITaskbarList3, THBF_DISABLED, THBF_ENABLED, THB_FLAGS, THB_ICON, THB_TOOLTIP,
    THUMBBUTTON, THUMBBUTTONFLAGS, TaskbarList,
};
use windows::Win32::UI::WindowsAndMessaging::{
    CallWindowProcW, DefWindowProcW, DestroyIcon, GetPropW, HICON, IMAGE_ICON, LR_LOADFROMFILE,
    LoadImageW, RegisterWindowMessageW, RemovePropW, SetPropW, SetWindowLongPtrW, GWLP_WNDPROC,
    WM_COMMAND, WM_NCDESTROY, WNDPROC,
};

const KG_BUTTON_PREVIOUS: u32 = 1001;
const KG_BUTTON_PLAYPAUSE: u32 = 1002;
const KG_BUTTON_NEXT: u32 = 1003;
const KG_BUTTON_LIKE: u32 = 1004;
const THBN_CLICKED_CODE: u16 = 0x1800;
const RPC_E_CHANGED_MODE_HRESULT: HRESULT = HRESULT(0x80010106u32 as i32);

type KgTaskbarButtonClickCallback = unsafe extern "system" fn(button_id: u32, user_data: *mut c_void);

#[repr(C)]
pub struct KgTaskbarToolbar {
    hwnd: HWND,
    original_wnd_proc: isize,
    taskbar: Option<ITaskbarList3>,
    previous_icon: HICON,
    play_icon: HICON,
    pause_icon: HICON,
    next_icon: HICON,
    heart_grey_icon: HICON,
    heart_red_icon: HICON,
    taskbar_button_created_message: u32,
    callback: Option<KgTaskbarButtonClickCallback>,
    user_data: *mut c_void,
    co_initialized: bool,
    is_playing: bool,
    is_liked: bool,
    buttons_added: bool,
}

impl KgTaskbarToolbar {
    fn new(hwnd: HWND, callback: Option<KgTaskbarButtonClickCallback>, user_data: *mut c_void) -> Self {
        Self {
            hwnd,
            original_wnd_proc: 0,
            taskbar: None,
            previous_icon: HICON::default(),
            play_icon: HICON::default(),
            pause_icon: HICON::default(),
            next_icon: HICON::default(),
            heart_grey_icon: HICON::default(),
            heart_red_icon: HICON::default(),
            taskbar_button_created_message: 0,
            callback,
            user_data,
            co_initialized: false,
            is_playing: false,
            is_liked: false,
            buttons_added: false,
        }
    }

    fn get_like_icon(&self) -> HICON {
        if self.is_liked {
            self.heart_red_icon
        } else {
            self.heart_grey_icon
        }
    }

    fn get_like_tooltip(&self) -> &'static str {
        if self.is_liked {
            "取消喜欢"
        } else {
            "我喜欢"
        }
    }
}

impl Drop for KgTaskbarToolbar {
    fn drop(&mut self) {
        unsafe {
            if self.hwnd.0 != std::ptr::null_mut() {
                let current = GetPropW(self.hwnd, w!("KugouTaskbarToolbar.Instance"));
                if current == HANDLE(self as *mut _ as *mut c_void) {
                    let _ = RemovePropW(self.hwnd, w!("KugouTaskbarToolbar.Instance"));
                }

                if self.original_wnd_proc != 0 {
                    let _ = SetWindowLongPtrW(self.hwnd, GWLP_WNDPROC, self.original_wnd_proc);
                    self.original_wnd_proc = 0;
                }
            }

            destroy_icon(&mut self.previous_icon);
            destroy_icon(&mut self.play_icon);
            destroy_icon(&mut self.pause_icon);
            destroy_icon(&mut self.next_icon);
            destroy_icon(&mut self.heart_grey_icon);
            destroy_icon(&mut self.heart_red_icon);

            if self.co_initialized {
                CoUninitialize();
                self.co_initialized = false;
            }
        }
    }
}

fn destroy_icon(icon: &mut HICON) {
    unsafe {
        if !icon.0.is_null() {
            let _ = DestroyIcon(*icon);
            *icon = HICON::default();
        }
    }
}

fn load_icon_from_file(path: PCWSTR) -> HICON {
    if path.is_null() {
        return HICON::default();
    }

    match unsafe { LoadImageW(None, path, IMAGE_ICON, 16, 16, LR_LOADFROMFILE) } {
        Ok(handle) => HICON(handle.0),
        Err(_) => HICON::default(),
    }
}

fn fill_thumb_button(
    button: &mut THUMBBUTTON,
    id: u32,
    icon: HICON,
    tooltip: &str,
    flags: THUMBBUTTONFLAGS,
) {
    *button = unsafe { zeroed() };
    button.dwMask = THB_ICON | THB_TOOLTIP | THB_FLAGS;
    button.iId = id;
    button.hIcon = icon;
    button.dwFlags = flags;

    let utf16: Vec<u16> = tooltip.encode_utf16().collect();
    let max = button.szTip.len().saturating_sub(1);
    let len = utf16.len().min(max);
    button.szTip[..len].copy_from_slice(&utf16[..len]);
    button.szTip[len] = 0;
}

fn add_buttons(toolbar: &KgTaskbarToolbar) -> WinResult<()> {
    let taskbar = toolbar.taskbar.as_ref().ok_or_else(windows::core::Error::from_win32)?;
    let mut buttons: [THUMBBUTTON; 4] = unsafe { zeroed() };

    fill_thumb_button(&mut buttons[0], KG_BUTTON_PREVIOUS, toolbar.previous_icon, "上一首", THBF_ENABLED);
    fill_thumb_button(
        &mut buttons[1],
        KG_BUTTON_PLAYPAUSE,
        if toolbar.is_playing { toolbar.pause_icon } else { toolbar.play_icon },
        if toolbar.is_playing { "暂停" } else { "播放" },
        THBF_ENABLED,
    );
    fill_thumb_button(&mut buttons[2], KG_BUTTON_NEXT, toolbar.next_icon, "下一首", THBF_ENABLED);
    fill_thumb_button(&mut buttons[3], KG_BUTTON_LIKE, toolbar.get_like_icon(), toolbar.get_like_tooltip(), THBF_DISABLED);

    unsafe { taskbar.ThumbBarAddButtons(toolbar.hwnd, &buttons) }
}

fn update_play_pause_core(toolbar: &KgTaskbarToolbar) -> WinResult<()> {
    let taskbar = toolbar.taskbar.as_ref().ok_or_else(windows::core::Error::from_win32)?;
    let mut button: THUMBBUTTON = unsafe { zeroed() };

    fill_thumb_button(
        &mut button,
        KG_BUTTON_PLAYPAUSE,
        if toolbar.is_playing { toolbar.pause_icon } else { toolbar.play_icon },
        if toolbar.is_playing { "暂停" } else { "播放" },
        THBF_ENABLED,
    );

    unsafe { taskbar.ThumbBarUpdateButtons(toolbar.hwnd, &[button]) }
}

fn update_enabled_core(
    toolbar: &KgTaskbarToolbar,
    previous_enabled: bool,
    play_pause_enabled: bool,
    next_enabled: bool,
) -> WinResult<()> {
    let taskbar = toolbar.taskbar.as_ref().ok_or_else(windows::core::Error::from_win32)?;
    let mut buttons: [THUMBBUTTON; 4] = unsafe { zeroed() };

    fill_thumb_button(
        &mut buttons[0],
        KG_BUTTON_PREVIOUS,
        toolbar.previous_icon,
        "上一首",
        if previous_enabled { THBF_ENABLED } else { THBF_DISABLED },
    );
    fill_thumb_button(
        &mut buttons[1],
        KG_BUTTON_PLAYPAUSE,
        if toolbar.is_playing { toolbar.pause_icon } else { toolbar.play_icon },
        if toolbar.is_playing { "暂停" } else { "播放" },
        if play_pause_enabled { THBF_ENABLED } else { THBF_DISABLED },
    );
    fill_thumb_button(
        &mut buttons[2],
        KG_BUTTON_NEXT,
        toolbar.next_icon,
        "下一首",
        if next_enabled { THBF_ENABLED } else { THBF_DISABLED },
    );
    fill_thumb_button(&mut buttons[3], KG_BUTTON_LIKE, toolbar.get_like_icon(), toolbar.get_like_tooltip(), THBF_DISABLED);

    unsafe { taskbar.ThumbBarUpdateButtons(toolbar.hwnd, &buttons) }
}

fn update_like_core(toolbar: &KgTaskbarToolbar, enabled: bool) -> WinResult<()> {
    let taskbar = toolbar.taskbar.as_ref().ok_or_else(windows::core::Error::from_win32)?;
    let mut button: THUMBBUTTON = unsafe { zeroed() };

    fill_thumb_button(
        &mut button,
        KG_BUTTON_LIKE,
        toolbar.get_like_icon(),
        toolbar.get_like_tooltip(),
        if enabled { THBF_ENABLED } else { THBF_DISABLED },
    );

    unsafe { taskbar.ThumbBarUpdateButtons(toolbar.hwnd, &[button]) }
}

fn original_wnd_proc(raw: isize) -> WNDPROC {
    unsafe { std::mem::transmute(raw) }
}

unsafe extern "system" fn taskbar_toolbar_wnd_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    let toolbar_handle = unsafe { GetPropW(hwnd, w!("KugouTaskbarToolbar.Instance")) };
    if toolbar_handle.0.is_null() {
        return unsafe { DefWindowProcW(hwnd, msg, wparam, lparam) };
    }

    let toolbar = unsafe { &mut *(toolbar_handle.0 as *mut KgTaskbarToolbar) };

    if msg == toolbar.taskbar_button_created_message {
        if !toolbar.buttons_added && add_buttons(toolbar).is_ok() {
            toolbar.buttons_added = true;
        }
    } else if msg == WM_COMMAND {
        let notification = ((wparam.0 >> 16) & 0xffff) as u16;
        let button_id = (wparam.0 & 0xffff) as u32;
        if notification == THBN_CLICKED_CODE
            && matches!(
                button_id,
                KG_BUTTON_PREVIOUS | KG_BUTTON_PLAYPAUSE | KG_BUTTON_NEXT | KG_BUTTON_LIKE
            )
        {
            if let Some(callback) = toolbar.callback {
                unsafe { callback(button_id, toolbar.user_data) };
            }
        }
    } else if msg == WM_NCDESTROY {
        if toolbar.original_wnd_proc != 0 {
            let _ = unsafe { SetWindowLongPtrW(hwnd, GWLP_WNDPROC, toolbar.original_wnd_proc) };
            toolbar.original_wnd_proc = 0;
        }
        let _ = unsafe { RemovePropW(hwnd, w!("KugouTaskbarToolbar.Instance")) };
    }

    if toolbar.original_wnd_proc != 0 {
        unsafe { CallWindowProcW(original_wnd_proc(toolbar.original_wnd_proc), hwnd, msg, wparam, lparam) }
    } else {
        unsafe { DefWindowProcW(hwnd, msg, wparam, lparam) }
    }
}

#[unsafe(no_mangle)]
pub extern "system" fn KgTaskbarToolbar_Create(
    hwnd: HWND,
    previous_icon_path: PCWSTR,
    play_icon_path: PCWSTR,
    pause_icon_path: PCWSTR,
    next_icon_path: PCWSTR,
    heart_grey_icon_path: PCWSTR,
    heart_red_icon_path: PCWSTR,
    callback: Option<KgTaskbarButtonClickCallback>,
    user_data: *mut c_void,
) -> *mut KgTaskbarToolbar {
    if hwnd.0 == std::ptr::null_mut() {
        return std::ptr::null_mut();
    }

    let mut toolbar = Box::new(KgTaskbarToolbar::new(hwnd, callback, user_data));
    toolbar.taskbar_button_created_message = unsafe { RegisterWindowMessageW(w!("TaskbarButtonCreated")) };

    toolbar.previous_icon = load_icon_from_file(previous_icon_path);
    toolbar.play_icon = load_icon_from_file(play_icon_path);
    toolbar.pause_icon = load_icon_from_file(pause_icon_path);
    toolbar.next_icon = load_icon_from_file(next_icon_path);
    toolbar.heart_grey_icon = load_icon_from_file(heart_grey_icon_path);
    toolbar.heart_red_icon = load_icon_from_file(heart_red_icon_path);

    if toolbar.previous_icon.0.is_null()
        || toolbar.play_icon.0.is_null()
        || toolbar.pause_icon.0.is_null()
        || toolbar.next_icon.0.is_null()
        || toolbar.heart_grey_icon.0.is_null()
        || toolbar.heart_red_icon.0.is_null()
    {
        return std::ptr::null_mut();
    }

    let co_init_result = unsafe { CoInitializeEx(None, COINIT_APARTMENTTHREADED) };
    if co_init_result.is_ok() {
        toolbar.co_initialized = true;
    } else if co_init_result != RPC_E_CHANGED_MODE_HRESULT {
        return std::ptr::null_mut();
    }

    let taskbar: ITaskbarList3 = match unsafe { CoCreateInstance(&TaskbarList, None, CLSCTX_INPROC_SERVER) } {
        Ok(taskbar) => taskbar,
        Err(_) => return std::ptr::null_mut(),
    };

    if unsafe { taskbar.HrInit() }.is_err() {
        return std::ptr::null_mut();
    }

    toolbar.taskbar = Some(taskbar);

    let toolbar_ptr = toolbar.as_mut() as *mut KgTaskbarToolbar;
    if unsafe { SetPropW(hwnd, w!("KugouTaskbarToolbar.Instance"), HANDLE(toolbar_ptr.cast())) }.is_err() {
        return std::ptr::null_mut();
    }

    unsafe { SetLastError(WIN32_ERROR(0)) };
    let original_wnd_proc =
        unsafe { SetWindowLongPtrW(hwnd, GWLP_WNDPROC, taskbar_toolbar_wnd_proc as *const () as _) };
    if original_wnd_proc == 0 && unsafe { GetLastError() } != WIN32_ERROR(0) {
        return std::ptr::null_mut();
    }

    toolbar.original_wnd_proc = original_wnd_proc;

    if add_buttons(&toolbar).is_ok() {
        toolbar.buttons_added = true;
    }

    Box::into_raw(toolbar)
}

#[unsafe(no_mangle)]
pub extern "system" fn KgTaskbarToolbar_UpdatePlayPause(toolbar: *mut KgTaskbarToolbar, is_playing: BOOL) {
    if toolbar.is_null() {
        return;
    }

    let toolbar = unsafe { &mut *toolbar };
    toolbar.is_playing = is_playing.as_bool();
    if toolbar.buttons_added {
        let _ = update_play_pause_core(toolbar);
    }
}

#[unsafe(no_mangle)]
pub extern "system" fn KgTaskbarToolbar_UpdateEnabled(
    toolbar: *mut KgTaskbarToolbar,
    previous_enabled: BOOL,
    play_pause_enabled: BOOL,
    next_enabled: BOOL,
    like_enabled: BOOL,
) {
    if toolbar.is_null() {
        return;
    }

    let toolbar = unsafe { &mut *toolbar };
    if !toolbar.buttons_added {
        return;
    }

    let _ = update_enabled_core(
        toolbar,
        previous_enabled.as_bool(),
        play_pause_enabled.as_bool(),
        next_enabled.as_bool(),
    );
    let _ = update_like_core(toolbar, like_enabled.as_bool());
}

#[unsafe(no_mangle)]
pub extern "system" fn KgTaskbarToolbar_UpdateLike(
    toolbar: *mut KgTaskbarToolbar,
    is_liked: BOOL,
    enabled: BOOL,
) {
    if toolbar.is_null() {
        return;
    }

    let toolbar = unsafe { &mut *toolbar };
    toolbar.is_liked = is_liked.as_bool();
    if toolbar.buttons_added {
        let _ = update_like_core(toolbar, enabled.as_bool());
    }
}

#[unsafe(no_mangle)]
pub extern "system" fn KgTaskbarToolbar_Destroy(toolbar: *mut KgTaskbarToolbar) {
    if toolbar.is_null() {
        return;
    }

    unsafe {
        drop(Box::from_raw(toolbar));
    }
}
