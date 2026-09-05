#if ANDROID
using System.Runtime.InteropServices;

namespace KuGou.Net.Native;

internal static partial class AndroidJniBootstrap
{
    private const string AndroidCryptoLibrary = "System.Security.Cryptography.Native.Android";

    [LibraryImport(AndroidCryptoLibrary, EntryPoint = "AndroidCryptoNative_InitLibraryOnLoad")]
    private static partial int InitializeAndroidCrypto(IntPtr javaVm, IntPtr reserved);

    // Flutter's DynamicLibrary.open uses dlopen directly, so Android never invokes JNI_OnLoad
    // unless the library is first loaded through System.loadLibrary on the JVM side.
    [UnmanagedCallersOnly(EntryPoint = "JNI_OnLoad")]
    public static int JniOnLoad(IntPtr javaVm, IntPtr reserved)
    {
        return InitializeAndroidCrypto(javaVm, reserved);
    }
}
#endif
