# AcsReader PC/SC compatibility shim

This Win32 DLL intentionally exports the six `acr_120*` functions consumed by the Be-Tech/Saga `btlock57L.dll`, but implements them with Windows PC/SC (`WinSCard`) so an ACS ACR122U can be used in place of the ACR120/RW-41 transport layer.

The codec remains the original authorized Be-Tech DLL. This shim does not reimplement the hotel-card format; it only translates reader operations:

- select / UID -> `FF CA 00 00 00`
- load key -> `FF 82 ...`
- authenticate -> `FF 86 ...`
- read block -> `FF B0 ...`
- write block -> `FF D6 ...`

Build only as **Win32/x86**. The `.def` file preserves the undecorated export names expected by the Delphi DLL.
