#include <jni.h>

extern JNIEXPORT jboolean JNICALL
Java_net_dot_android_crypto_DotnetProxyTrustManager_verifyRemoteCertificate(
    JNIEnv* env,
    jclass type,
    jlong sslStreamProxyHandle);

JNIEXPORT jint JNICALL KugouAndroidRegisterNatives(JavaVM* javaVm)
{
    JNIEnv* env = NULL;
    if ((*javaVm)->GetEnv(javaVm, (void**)&env, JNI_VERSION_1_6) != JNI_OK || env == NULL)
        return JNI_ERR;

    jclass trustManager = (*env)->FindClass(
        env,
        "net/dot/android/crypto/DotnetProxyTrustManager");
    if (trustManager == NULL)
        return JNI_ERR;

    JNINativeMethod methods[] = {
        {
            "verifyRemoteCertificate",
            "(J)Z",
            (void*)Java_net_dot_android_crypto_DotnetProxyTrustManager_verifyRemoteCertificate
        }
    };

    jint result = (*env)->RegisterNatives(env, trustManager, methods, 1);
    (*env)->DeleteLocalRef(env, trustManager);
    return result == JNI_OK ? JNI_OK : JNI_ERR;
}
