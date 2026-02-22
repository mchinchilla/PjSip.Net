/*
 * config_site.h — PJSIP build configuration for iOS arm64.
 *
 * This file is copied into pjlib/include/pj/config_site.h before building.
 * It configures a static/shared-library build of pjsua2 with:
 *   - Apple Secure Transport / built-in TLS (no OpenSSL dependency)
 *   - pjsua2 C++ API enabled
 *   - iOS audio route handling via AudioUnit
 *   - Video support disabled
 */

#ifndef __PJ_CONFIG_SITE_H__
#define __PJ_CONFIG_SITE_H__

/* ---------------------------------------------------------------------------
 * iOS-specific flag — MUST be defined BEFORE including config_site_sample.h
 * so that the sample header activates its iPhone-specific defaults
 * (CoreAudio AEC, audio device settings, etc.).
 * -------------------------------------------------------------------------*/

/* Mark this as an iPhone build so pjlib uses the correct APIs. */
#define PJ_CONFIG_IPHONE               1

/* Include the base iOS configuration provided by pjproject.
 * With PJ_CONFIG_IPHONE=1 already defined, this will enable:
 *   - CoreAudio audio device (PJMEDIA_AUDIO_DEV_HAS_COREAUDIO)
 *   - iOS-native AEC (disables Speex AEC)
 *   - Proper iPhone audio defaults
 */
#include <pj/config_site_sample.h>

/* ---------------------------------------------------------------------------
 * General build mode
 * -------------------------------------------------------------------------*/

/* Build as a shared library (.dylib). */
#define PJ_DLL                          1
#define PJ_EXPORTING                    1

/* ---------------------------------------------------------------------------
 * TLS / SSL — Apple Secure Transport (built-in, no OpenSSL)
 * -------------------------------------------------------------------------*/

/* Use the Darwin SSL socket implementation which wraps Apple's Secure
 * Transport framework. This removes the OpenSSL dependency entirely. */
#define PJ_SSL_SOCK_IMP                 PJ_SSL_SOCK_IMP_DARWIN
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
 * iOS-specific audio settings and route handling
 * -------------------------------------------------------------------------*/

/* Explicitly ensure CoreAudio is enabled (also set by config_site_sample.h). */
#define PJMEDIA_AUDIO_DEV_HAS_COREAUDIO 1

/* Disable alternative audio backends. */
#define PJMEDIA_AUDIO_DEV_HAS_PORTAUDIO 0
#define PJMEDIA_AUDIO_DEV_HAS_WMME      0

/* iOS background audio — use PushKit for modern iOS (13+).
 * Setting to 0 prevents PJSIP from maintaining background sockets
 * which can interfere with iOS audio session management. */
#define PJ_IPHONE_OS_HAS_MULTITASKING_SUPPORT  0

/* Enable activation of the iOS audio session on incoming calls. */
#define PJSUA_DETECT_MERGED_REQUESTS    1

/* ---------------------------------------------------------------------------
 * Performance / size optimizations
 * -------------------------------------------------------------------------*/

#define PJMEDIA_HAS_SRTP               1
#define PJSUA_MAX_CALLS                8
#define PJSIP_MAX_TSX_COUNT            255
#define PJSIP_MAX_DIALOG_COUNT         127
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
#define PJMEDIA_HAS_BCG729            1

/* Disable video-related codec dependencies. */
#define PJMEDIA_HAS_LIBYUV            0

/* ---------------------------------------------------------------------------
 * Transport
 * -------------------------------------------------------------------------*/

#define PJSIP_HAS_TLS_TRANSPORT        1

#endif /* __PJ_CONFIG_SITE_H__ */
