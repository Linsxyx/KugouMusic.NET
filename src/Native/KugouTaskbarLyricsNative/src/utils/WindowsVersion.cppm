module;

#include <Windows.h>
#include <string>

export module utils.WindowsVersion;

export class WindowsVersion {
private:
    static inline DWORD majorVersion = 0;
    static inline DWORD minorVersion = 0;
    static inline DWORD buildNumber = 0;
    static inline bool initialized = false;

public:
    static auto initialize() -> void {
        if (initialized) {
            return;
        }

        // 使用 RtlGetVersion 获取真实的 Windows 版本
        typedef LONG (WINAPI* RtlGetVersionPtr)(PRTL_OSVERSIONINFOW);
        HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
        if (ntdll) {
            RtlGetVersionPtr RtlGetVersion = (RtlGetVersionPtr)GetProcAddress(ntdll, "RtlGetVersion");
            if (RtlGetVersion) {
                RTL_OSVERSIONINFOW versionInfo = { 0 };
                versionInfo.dwOSVersionInfoSize = sizeof(RTL_OSVERSIONINFOW);
                if (RtlGetVersion(&versionInfo) == 0) {
                    majorVersion = versionInfo.dwMajorVersion;
                    minorVersion = versionInfo.dwMinorVersion;
                    buildNumber = versionInfo.dwBuildNumber;
                    initialized = true;
                }
            }
        }
    }

    static auto isWindows10() -> bool {
        if (!initialized) initialize();
        // Windows 10: version 10.0, build < 22000
        return majorVersion == 10 && minorVersion == 0 && buildNumber < 22000;
    }

    static auto isWindows11() -> bool {
        if (!initialized) initialize();
        // Windows 11: version 10.0, build >= 22000
        return majorVersion == 10 && minorVersion == 0 && buildNumber >= 22000;
    }

    static auto getMajorVersion() -> DWORD {
        if (!initialized) initialize();
        return majorVersion;
    }

    static auto getMinorVersion() -> DWORD {
        if (!initialized) initialize();
        return minorVersion;
    }

    static auto getBuildNumber() -> DWORD {
        if (!initialized) initialize();
        return buildNumber;
    }

    static auto toString() -> std::string {
        if (!initialized) initialize();
        if (isWindows11()) {
            return "Windows 11";
        } else if (isWindows10()) {
            return "Windows 10";
        } else if (majorVersion == 6 && minorVersion == 1) {
            return "Windows 7";
        } else {
            return "Windows " + std::to_string(majorVersion) + "." + std::to_string(minorVersion);
        }
    }
};