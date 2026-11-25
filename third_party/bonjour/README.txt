dns_sd.h from Apple's open-source mDNSResponder (BSD 3-clause license, see header).
Source: https://github.com/apple-oss-distributions/mDNSResponder/blob/main/mDNSShared/dns_sd.h
Downloaded 2026-11-25. Used only to compile UxPlay with -DUSE_DNS_SD=1; at runtime UxPlay loads
dnssd.dll of the installed Apple "Bonjour Service" via LoadLibrary (it is not bundled).
