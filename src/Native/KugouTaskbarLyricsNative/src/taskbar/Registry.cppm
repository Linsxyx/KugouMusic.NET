module;

#include <Windows.h>
#include <thread>
#include <functional>

export module taskbar.Registry;

export class Registry {
public:
    static auto isTaskbarCentered() -> bool {
        HKEY hKey;
        bool result = false;
        if (RegOpenKeyEx(HKEY_CURRENT_USER,
            LR"(Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced)",
            0, KEY_READ, &hKey) == ERROR_SUCCESS) {
            DWORD value = 0;
            DWORD size = sizeof(value);
            if (RegQueryValueEx(hKey, L"TaskbarAl", nullptr, nullptr, (LPBYTE)&value, &size) == ERROR_SUCCESS) {
                result = (value == 1);
            }
            RegCloseKey(hKey);
        }
        return result;
    }

    static auto isWidgetsEnabled() -> bool {
        HKEY hKey;
        bool result = false;
        if (RegOpenKeyEx(HKEY_CURRENT_USER,
            LR"(Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced)",
            0, KEY_READ, &hKey) == ERROR_SUCCESS) {
            DWORD value = 1;
            DWORD size = sizeof(value);
            RegQueryValueEx(hKey, L"TaskbarDa", nullptr, nullptr, (LPBYTE)&value, &size);
            result = (value != 0);
            RegCloseKey(hKey);
        }
        return result;
    }

    static auto onWatch(const std::function<void()> &callback) -> void {
        std::thread([callback] {
            HKEY hKey;
            if (RegOpenKeyEx(HKEY_CURRENT_USER,
                LR"(Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced)",
                0, KEY_NOTIFY, &hKey) != ERROR_SUCCESS) {
                return;
            }

            while (true) {
                HANDLE hEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
                if (RegNotifyChangeKeyValue(hKey, FALSE, 0x00000004L, hEvent, TRUE) == ERROR_SUCCESS) {
                    WaitForSingleObject(hEvent, INFINITE);
                    callback();
                }
                CloseHandle(hEvent);
            }
        }).detach();
    }
};
