//! Shared COM scaffold for the Shell-dispatch operations (Quick Access and
//! Recycle Bin). The two operations previously carried byte-identical copies
//! of this prelude; the scaffold is macro-parameterized over the operation's
//! phase bits because those constants differ per ABI contract (R1-054). The
//! per-operation `finish` functions and result structs stay in their own
//! modules — the ABI export surface is frozen and unaffected.

use std::slice;

use windows::{
    Win32::{
        Globalization::{CSTR_EQUAL, CompareStringOrdinal},
        Storage::FileSystem::GetFullPathNameW,
        System::Com::CoUninitialize,
    },
    core::{Error, PCWSTR},
};

use crate::DeskBoxNativeUtf16StringV1;

pub(crate) struct ComUninitializeGuard {
    pub(crate) active: bool,
}

impl Drop for ComUninitializeGuard {
    fn drop(&mut self) {
        if self.active {
            // SAFETY: Each active guard represents one successful CoInitializeEx call.
            unsafe { CoUninitialize() };
        }
    }
}

/// Runs the shared COM prelude: initializes COM, creates the Shell.Application
/// object, and records phase bits and HRESULTs on the caller's result struct
/// (the struct type differs per operation, but the field names are part of
/// both ABI contracts). On success returns the shell interface together with
/// the COM uninitialization guard, which the caller must keep alive for the
/// rest of the operation. On failure returns `(status, hresult)` for the
/// caller's module-local `finish`.
macro_rules! com_shell_prelude {
    ($result:expr, $phase_com_initialize:expr, $phase_create_object:expr) => {{
        // All paths are fully qualified because macro_rules resolves free
        // identifiers at the call site, not here.
        $result.attempted_phases |= $phase_com_initialize;
        // SAFETY: COM initialization is balanced by the guard for S_OK and S_FALSE.
        let com_hresult = unsafe {
            ::windows::Win32::System::Com::CoInitializeEx(
                None,
                ::windows::Win32::System::Com::COINIT_APARTMENTTHREADED,
            )
        };
        $result.com_hresult = com_hresult.0;
        let should_uninitialize = com_hresult.0 == $crate::DESKBOX_NATIVE_S_OK
            || com_hresult.0 == $crate::DESKBOX_NATIVE_S_FALSE;
        if !should_uninitialize
            && com_hresult.0 != ::windows::Win32::Foundation::RPC_E_CHANGED_MODE.0
        {
            Err((
                $crate::DESKBOX_NATIVE_STATUS_COM_INITIALIZATION_FAILED,
                com_hresult.0,
            ))
        } else {
            'prelude: {
                let com_guard = $crate::com_scaffold::ComUninitializeGuard {
                    active: should_uninitialize,
                };
                $result.attempted_phases |= $phase_create_object;
                // SAFETY: Shell is the documented Shell.Application coclass and the requested
                // interface is generated from the Windows Shell type library.
                let shell: ::windows::Win32::UI::Shell::IShellDispatch = match unsafe {
                    ::windows::Win32::System::Com::CoCreateInstance(
                        &::windows::Win32::UI::Shell::Shell,
                        None,
                        ::windows::Win32::System::Com::CLSCTX_ALL,
                    )
                } {
                    Ok(value) => {
                        $result.create_hresult = $crate::DESKBOX_NATIVE_S_OK;
                        value
                    }
                    Err(error) => {
                        let code = error.code().0;
                        $result.create_hresult = code;
                        // Break out of the labeled else-block with the failure
                        // for the caller's outer match (a plain `return` here
                        // would target the caller's function instead).
                        break 'prelude Err((
                            $crate::DESKBOX_NATIVE_STATUS_OBJECT_CREATION_FAILED,
                            code,
                        ));
                    }
                };
                Ok((shell, com_guard))
            }
        }
    }};
}

pub(crate) use com_shell_prelude;

pub(crate) unsafe fn input_slice(value: &DeskBoxNativeUtf16StringV1) -> &[u16] {
    if value.length_chars == 0 {
        &[]
    } else {
        // SAFETY: ABI validation requires a readable pointer for every non-empty input.
        unsafe { slice::from_raw_parts(value.data, value.length_chars as usize) }
    }
}

pub(crate) fn normalize_full_path(path: &[u16]) -> Result<Vec<u16>, i32> {
    let mut terminated = Vec::with_capacity(path.len() + 1);
    terminated.extend_from_slice(path);
    terminated.push(0);

    // SAFETY: terminated is a readable, NUL-terminated UTF-16 buffer.
    let required = unsafe { GetFullPathNameW(PCWSTR(terminated.as_ptr()), None, None) };
    if required == 0 {
        return Err(Error::from_thread().code().0);
    }

    let mut buffer = vec![0_u16; required as usize];
    // SAFETY: terminated remains alive and buffer is writable for its full capacity.
    let written = unsafe {
        GetFullPathNameW(
            PCWSTR(terminated.as_ptr()),
            Some(buffer.as_mut_slice()),
            None,
        )
    };
    if written == 0 {
        return Err(Error::from_thread().code().0);
    }
    if written as usize >= buffer.len() {
        buffer.resize(written as usize + 1, 0);
        // SAFETY: terminated remains alive and the resized buffer is writable.
        let retried = unsafe {
            GetFullPathNameW(
                PCWSTR(terminated.as_ptr()),
                Some(buffer.as_mut_slice()),
                None,
            )
        };
        if retried == 0 || retried as usize >= buffer.len() {
            return Err(Error::from_thread().code().0);
        }
        buffer.truncate(retried as usize);
        return Ok(buffer);
    }

    buffer.truncate(written as usize);
    Ok(buffer)
}

pub(crate) fn paths_equal(left: &[u16], right: &[u16]) -> bool {
    let left = trim_directory_separators(left);
    let right = trim_directory_separators(right);
    // SAFETY: Both slices are readable for their declared lengths.
    unsafe { CompareStringOrdinal(left, right, true) == CSTR_EQUAL }
}

fn trim_directory_separators(mut value: &[u16]) -> &[u16] {
    while value
        .last()
        .is_some_and(|last| *last == b'\\' as u16 || *last == b'/' as u16)
    {
        value = &value[..value.len() - 1];
    }
    value
}
