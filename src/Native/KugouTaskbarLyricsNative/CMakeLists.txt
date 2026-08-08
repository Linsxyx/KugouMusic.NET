cmake_minimum_required(VERSION 3.30)

if(NOT WIN32)
    message(FATAL_ERROR "KugouTaskbarLyricsNative is a Windows-only target.")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_EXPERIMENTAL_CXX_MODULE_CMAKE_API "2182bf5c-ef0d-489a-91da-49dbc3090d2a")
set(CMAKE_EXPERIMENTAL_CXX_MODULE_DYNDEP ON)

add_compile_options(/MT /utf-8 /D_UNICODE /DUNICODE /DNOMINMAX)


project(KugouTaskbarLyricsNative LANGUAGES CXX)

add_executable(KugouTaskbarLyricsNative WIN32)

target_sources(KugouTaskbarLyricsNative
    PUBLIC
        FILE_SET CXX_MODULES FILES
            "src/plugin/Config.cppm"
            "src/taskbar/Registry.cppm"
            "src/taskbar/Handler.cppm"
            "src/taskbar/Taskbar.cppm"
            "src/window/Lyrics.cppm"
            "src/window/Renderer.cppm"
            "src/window/Window.cppm"
            "src/window/WindowWin10.cppm"
            "src/utils/WindowsVersion.cppm"
            "src/plugin/Plugin.cppm"
    PRIVATE
        "src/Main.cpp"
)

target_link_libraries(KugouTaskbarLyricsNative PRIVATE d3d11 d2d1 dwrite dxgi dcomp dwmapi UIAutomationCore oleacc)
