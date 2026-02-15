/*
 * config_site.h — PJSIP build configuration for Windows x64 with Schannel TLS.
 *
 * This file is copied into pjlib/include/pj/config_site.h before building.
 * It configures a Release-Dynamic (DLL) build of pjsua2 with:
 *   - Schannel as the TLS backend (no OpenSSL dependency)
 *   - pjsua2 C++ API enabled
 *   - Video support disabled
 *   - Standard performance optimizations
 */

#ifndef __PJ_CONFIG_SITE_H__
#define __PJ_CONFIG_SITE_H__

/* ---------------------------------------------------------------------------
 * General build mode
 * -------------------------------------------------------------------------*/

/* Build as a dynamic library (DLL). PJ_EXPORTING is set by the project
 * producing the DLL; consumers define PJ_DLL instead. */
#define PJ_DLL                          1
#define PJ_EXPORTING                    1

/* ---------------------------------------------------------------------------
 * TLS / SSL — use Windows Schannel (no OpenSSL)
 * -------------------------------------------------------------------------*/

/* Select the Schannel SSL socket implementation. */
#define PJ_SSL_SOCK_IMP                 PJ_SSL_SOCK_IMP_SCHANNEL

/* Explicitly disable OpenSSL so the build does not accidentally link it. */
#define PJ_HAS_SSL_SOCK                 1

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
 * Performance / size optimizations
 * -------------------------------------------------------------------------*/

/* Use the optimized third-party SRTP bundled with pjproject. */
#define PJMEDIA_HAS_SRTP               1

/* Increase default number of simultaneous calls. */
#define PJSUA_MAX_CALLS                 32

/* Increase SIP transaction hash table for better scalability. */
#define PJSIP_MAX_TSX_COUNT             1023

/* Increase dialog hash table. */
#define PJSIP_MAX_DIALOG_COUNT          511

/* Logging: keep info level by default; can be overridden at runtime. */
#define PJ_LOG_MAX_LEVEL                5

/* Disable assertions in Release builds for performance. */
#ifndef _DEBUG
#define PJ_ENABLE_EXTRA_CHECK           0
#endif

/* ---------------------------------------------------------------------------
 * Codec support
 * -------------------------------------------------------------------------*/

/* Enable the common audio codecs. */
#define PJMEDIA_HAS_G711_CODEC         1
#define PJMEDIA_HAS_GSM_CODEC          1
#define PJMEDIA_HAS_SPEEX_CODEC        1
#define PJMEDIA_HAS_ILBC_CODEC         1
#define PJMEDIA_HAS_OPUS_CODEC         1
#define PJMEDIA_HAS_BCG729             1

/* ---------------------------------------------------------------------------
 * Transport
 * -------------------------------------------------------------------------*/

/* Enable TCP and TLS transports. */
#define PJSIP_HAS_TLS_TRANSPORT        1

#endif /* __PJ_CONFIG_SITE_H__ */
