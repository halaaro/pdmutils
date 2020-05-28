# pdmutils
Simple SOLIDWORKS PDM Utilities


## pdmrevertfile
`Usage: PdmRevertFile.exe <FILE_PATH> <VERSION_NO> <CHECKIN_COMMENT> [CHECK_VERSION_NO]`

Utility to revert file without loss of history (ie. no rollback). Works by checking out file, overwriting with the specified `VERSION_NO`, and then finally checking the file back in. `CHECKIN_COMMENT` is used in history and for checking that this file has not yet been reverted. Doing this ensures idempotency in the case of multiple runs.

**Safeguards**

`CHECKIN_COMMENT` - If this matches the latest version, the revert will fail. Make sure you use a unique comment each time to avoid this.

`CHECK_VERSION_NO` - Optional sanity check to make sure the latest version is the `CHECK_VERSION_NO`. If not it does nothing.
