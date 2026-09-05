#ifndef KUGOU_NET_NATIVE_H
#define KUGOU_NET_NATIVE_H

#include <stdint.h>

#if defined(_WIN32)
#define KG_API __declspec(dllimport)
#else
#define KG_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Returns an allocated JSON result. Release it with KgFreeMemory. */
KG_API void *KgInitSdk(void);

/* Starts a request and returns immediately with a positive request id. */
KG_API int64_t KgRequestStart(const char *request_json_utf8);

/* Returns allocated {"state":"pending|completed|failed", ...} JSON. */
KG_API void *KgRequestPoll(int64_t request_id);

/* Stops tracking the result. Returns 1 when the id existed, otherwise 0. */
KG_API int32_t KgRequestCancel(int64_t request_id);

KG_API void KgFreeMemory(void *pointer);

#ifdef __cplusplus
}
#endif

#endif
