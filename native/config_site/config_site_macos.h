/*
 * config_site.h — PJSIP build configuration for macOS (x64 and arm64).
 *
 * This file is copied into pjlib/include/pj/config_site.h before building.
 * It configures a shared-library build of pjsua2 with:
 *   - OpenSSL as the TLS backend (installed via Homebrew)
 *   - pjsua2 C++ API enabled
 *   - Video support disabled
 *   - CoreAudio for media
 */

#ifndef __PJ_CONFIG_SITE_H__
#define __PJ_CONFIG_SITE_H__

/* ---------------------------------------------------------------------------
 * General build mode
 * -------------------------------------------------------------------------*/

/* Build as a shared library (.dylib). */
#define PJ_DLL                          1
#define PJ_EXPORTING                    1

/* ---------------------------------------------------------------------------
 * TLS / SSL — use OpenSSL (Homebrew)
 * -------------------------------------------------------------------------*/

#define PJ_HAS_SSL_SOCK                 1

/* Use the default OpenSSL SSL socket implementation. On macOS with Homebrew,
 * OpenSSL headers and libs are found at /opt/homebrew/opt/openssl (arm64) or
 * /usr/local/opt/openssl (x64). Pass the correct --with-ssl= to configure. */

/* ---------------------------------------------------------------------------
 * pjsua2 C++ high-level API
 * -------------------------------------------------------------------------*/

#define PJSUA2_ENABLED                  1
#define PJ_HAS_THREADS                  1

/* ---------------------------------------------------------------------------
 * Media / Video — disabled to reduce dependencies
 * -------------------------------------------------------------------------*/

#define PJMEDIA_HAS_VIDEO              0

/* ---------------------------------------------------------------------------
 * macOS-specific audio
 * -------------------------------------------------------------------------*/

/* Use CoreAudio for the audio device. This is the default on macOS but
 * we make it explicit here. */
#define PJMEDIA_AUDIO_DEV_HAS_COREAUDIO 1

/* ---------------------------------------------------------------------------
 * Performance / size optimizations
 * -------------------------------------------------------------------------*/

#define PJMEDIA_HAS_SRTP               1
#define PJSUA_MAX_CALLS                32
#define PJSIP_MAX_TSX_COUNT            1023
#define PJSIP_MAX_DIALOG_COUNT         511
#define PJ_LOG_MAX_LEVEL               5

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

/* ---------------------------------------------------------------------------
 * Transport
 * -------------------------------------------------------------------------*/

#define PJSIP_HAS_TLS_TRANSPORT        1

#endif /* __PJ_CONFIG_SITE_H__ */
