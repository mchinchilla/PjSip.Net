/* PjSip.Net — shared config_site.h for all platforms
 *
 * Copy this file to: pjproject/pjlib/include/pj/config_site.h
 * before building PJSIP on any platform.
 */
#ifndef __PJ_CONFIG_SITE_H__
#define __PJ_CONFIG_SITE_H__

/* TLS/SSL Support */
#define PJ_HAS_SSL_SOCK 1

/* Windows: use Schannel (native TLS, no OpenSSL dependency) */
#if defined(_WIN32) || defined(_WIN64)
  #define PJ_SSL_SOCK_IMP PJ_SSL_SOCK_IMP_SCHANNEL

  /* DLL export (required for P/Invoke from C#) */
  #define PJ_EXPORT_SPECIFIER  __declspec(dllexport)
  #define PJ_IMPORT_SPECIFIER  __declspec(dllimport)
  #define PJ_DLL 1
  #ifdef _LIB
    #define PJ_EXPORTING 1
  #endif
#endif

/* macOS / iOS: use Apple SecureTransport or OpenSSL via Homebrew */
#if defined(__APPLE__)
  /* Darwin SSL is deprecated; OpenSSL from Homebrew recommended */
#endif

/* Android: use OpenSSL from NDK sysroot */
#if defined(__ANDROID__)
  #define PJ_IS_BIG_ENDIAN 0
  #define PJ_IS_LITTLE_ENDIAN 1
#endif

#endif /* __PJ_CONFIG_SITE_H__ */
