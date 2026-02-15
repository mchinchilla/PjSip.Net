/*
 * config_site.h — PJSIP build configuration for Android arm64.
 *
 * This file is copied into pjlib/include/pj/config_site.h before building.
 * It configures a shared-library build of pjsua2 with:
 *   - OpenSSL TLS (built alongside the NDK toolchain)
 *   - pjsua2 C++ API enabled
 *   - Oboe/OpenSLES audio device (OAUD)
 *   - Video support disabled
 */

#ifndef __PJ_CONFIG_SITE_H__
#define __PJ_CONFIG_SITE_H__

/* Include the base Android configuration provided by pjproject. */
#include <pj/config_site_sample.h>

/* ---------------------------------------------------------------------------
 * General build mode
 * -------------------------------------------------------------------------*/

/* Build as a shared library (.so). */
#define PJ_DLL                          1
#define PJ_EXPORTING                    1

/* ---------------------------------------------------------------------------
 * TLS / SSL — OpenSSL
 * -------------------------------------------------------------------------*/

#define PJ_HAS_SSL_SOCK                 1

/* ---------------------------------------------------------------------------
 * pjsua2 C++ high-level API
 * -------------------------------------------------------------------------*/

#define PJSUA2_ENABLED                  1
#define PJ_HAS_THREADS                  1

/* ---------------------------------------------------------------------------
 * Media / Video — disabled
 * -------------------------------------------------------------------------*/

#define PJMEDIA_HAS_VIDEO              0

/* ---------------------------------------------------------------------------
 * Android-specific audio settings
 * -------------------------------------------------------------------------*/

/* Use the Oboe audio device (OAUD) which wraps AAudio/OpenSL ES and
 * provides low-latency audio on modern Android devices. */
#define PJMEDIA_AUDIO_DEV_HAS_OBOE             1
#define PJMEDIA_AUDIO_DEV_HAS_ANDROID_JNI      0
#define PJMEDIA_AUDIO_DEV_HAS_OPENSL           1

/* Set the audio device to use Oboe as the primary implementation. */
#define PJMEDIA_AUDIO_DEV_HAS_WMME             0
#define PJMEDIA_AUDIO_DEV_HAS_PORTAUDIO        0

/* ---------------------------------------------------------------------------
 * Performance / size optimizations
 * -------------------------------------------------------------------------*/

#define PJMEDIA_HAS_SRTP               1
#define PJSUA_MAX_CALLS                16
#define PJSIP_MAX_TSX_COUNT            511
#define PJSIP_MAX_DIALOG_COUNT         255
#define PJ_LOG_MAX_LEVEL               4

#ifndef NDEBUG
#define PJ_ENABLE_EXTRA_CHECK          0
#endif

/* ---------------------------------------------------------------------------
 * Codec support
 * -------------------------------------------------------------------------*/

#define PJMEDIA_HAS_G711_CODEC         1
#define PJMEDIA_HAS_GSM_CODEC         1
#define PJMEDIA_HAS_SPEEX_CODEC       1
#define PJMEDIA_HAS_ILBC_CODEC        1
#define PJMEDIA_HAS_OPUS_CODEC        1

/* Use the bundled third-party Ogg for Opus. */
#define PJMEDIA_HAS_LIBYUV            0

/* ---------------------------------------------------------------------------
 * Transport
 * -------------------------------------------------------------------------*/

#define PJSIP_HAS_TLS_TRANSPORT        1

/* ---------------------------------------------------------------------------
 * Android JNI thread registration
 * -------------------------------------------------------------------------*/

/* Ensure SRTP is not using pjlib threading for random numbers, which
 * can cause issues on Android without proper JNI attachment. */
#define PJ_ANDROID                     1

#endif /* __PJ_CONFIG_SITE_H__ */
