use windows::{
    Win32::{
        Globalization::{CSTR_EQUAL, CompareStringOrdinal},
        System::Variant::{VARIANT, VT_BSTR},
        UI::Shell::{FolderItem, FolderItem2},
    },
    core::{BSTR, Interface},
};

use crate::com_scaffold::{com_shell_prelude, input_slice, normalize_full_path, paths_equal};
use crate::{
    DESKBOX_NATIVE_S_OK, DESKBOX_NATIVE_STATUS_OK, DESKBOX_NATIVE_STATUS_OPERATION_FAILED,
    DESKBOX_RECYCLE_BIN_OPERATION_RESTORE, DESKBOX_RECYCLE_BIN_PHASE_COM_INITIALIZE,
    DESKBOX_RECYCLE_BIN_PHASE_CREATE_OBJECT, DESKBOX_RECYCLE_BIN_PHASE_ENUMERATE,
    DESKBOX_RECYCLE_BIN_PHASE_INVOKE, DESKBOX_RECYCLE_BIN_PHASE_ITEM_NAME,
    DESKBOX_RECYCLE_BIN_PHASE_ITEMS, DESKBOX_RECYCLE_BIN_PHASE_NAMESPACE,
    DESKBOX_RECYCLE_BIN_PHASE_PROPERTY, DeskBoxRecycleBinRequestV1, DeskBoxRecycleBinResultV1,
};

const RECYCLE_BIN_CSIDL: i32 = 10;
const DELETED_FROM_PROPERTY: &str = "System.Recycle.DeletedFrom";
const RESTORE_VERB: &str = "undelete";

pub(crate) unsafe fn execute(
    request: &DeskBoxRecycleBinRequestV1,
    result: &mut DeskBoxRecycleBinResultV1,
) -> u32 {
    let (shell, _com_guard) = match com_shell_prelude!(
        result,
        DESKBOX_RECYCLE_BIN_PHASE_COM_INITIALIZE,
        DESKBOX_RECYCLE_BIN_PHASE_CREATE_OBJECT
    ) {
        Ok(prelude) => prelude,
        Err((status, hresult)) => return finish(result, status, hresult, false),
    };

    result.attempted_phases |= DESKBOX_RECYCLE_BIN_PHASE_NAMESPACE;
    let namespace_value = VARIANT::from(RECYCLE_BIN_CSIDL);
    // SAFETY: The namespace VARIANT remains alive for the synchronous COM call.
    let recycle_bin = match unsafe { shell.NameSpace(&namespace_value) } {
        Ok(value) => {
            result.namespace_hresult = DESKBOX_NATIVE_S_OK;
            value
        }
        Err(error) => {
            result.namespace_hresult = error.code().0;
            return finish(
                result,
                DESKBOX_NATIVE_STATUS_OPERATION_FAILED,
                error.code().0,
                false,
            );
        }
    };

    result.attempted_phases |= DESKBOX_RECYCLE_BIN_PHASE_ITEMS;
    // SAFETY: The generated Folder wrapper owns the returned collection.
    let items = match unsafe { recycle_bin.Items() } {
        Ok(value) => {
            result.items_hresult = DESKBOX_NATIVE_S_OK;
            value
        }
        Err(error) => {
            result.items_hresult = error.code().0;
            return finish(
                result,
                DESKBOX_NATIVE_STATUS_OPERATION_FAILED,
                error.code().0,
                false,
            );
        }
    };

    result.attempted_phases |= DESKBOX_RECYCLE_BIN_PHASE_ENUMERATE;
    // SAFETY: The collection remains valid for this synchronous enumeration.
    let count = match unsafe { items.Count() } {
        Ok(value) => value.max(0),
        Err(error) => {
            result.enumerate_hresult = error.code().0;
            return finish(
                result,
                DESKBOX_NATIVE_STATUS_OPERATION_FAILED,
                error.code().0,
                false,
            );
        }
    };

    // SAFETY: ABI validation guarantees both input slices remain readable.
    let expected_parent = unsafe { input_slice(&request.original_parent) };
    // SAFETY: ABI validation guarantees both input slices remain readable.
    let expected_name = unsafe { input_slice(&request.original_name) };
    let normalized_parent = match normalize_full_path(expected_parent) {
        Ok(value) => value,
        Err(hresult) => {
            result.property_hresult = hresult;
            return finish(
                result,
                DESKBOX_NATIVE_STATUS_OPERATION_FAILED,
                hresult,
                false,
            );
        }
    };

    let mut restore_item: Option<FolderItem> = None;
    for index in 0..count {
        let index_variant = VARIANT::from(index);
        // SAFETY: The index VARIANT remains alive for the synchronous collection call.
        // ABI contract §2: any item whose identity cannot be fully read makes the
        // whole operation fail conservatively; it must never degrade into a
        // zero-match or single-match result that could still reach `undelete`.
        let item = match unsafe { items.Item(&index_variant) } {
            Ok(value) => value,
            Err(error) => {
                result.enumerate_hresult = error.code().0;
                return finish(
                    result,
                    DESKBOX_NATIVE_STATUS_OPERATION_FAILED,
                    error.code().0,
                    false,
                );
            }
        };

        match unsafe { item_name_matches(&item, expected_name, result) } {
            Ok(true) => {}
            Ok(false) => continue,
            Err(hresult) => {
                return finish(
                    result,
                    DESKBOX_NATIVE_STATUS_OPERATION_FAILED,
                    hresult,
                    false,
                );
            }
        }
        match unsafe { deleted_from_matches(&item, &normalized_parent, result) } {
            Ok(true) => {}
            Ok(false) => continue,
            Err(hresult) => {
                return finish(
                    result,
                    DESKBOX_NATIVE_STATUS_OPERATION_FAILED,
                    hresult,
                    false,
                );
            }
        }

        result.matched_count += 1;
        if request.operation == DESKBOX_RECYCLE_BIN_OPERATION_RESTORE && result.matched_count == 1 {
            restore_item = Some(item);
        }
    }

    result.enumerate_hresult = DESKBOX_NATIVE_S_OK;
    if request.operation == DESKBOX_RECYCLE_BIN_OPERATION_RESTORE {
        if result.matched_count != 1 {
            let hresult = if result.matched_count == 0 {
                windows::core::HRESULT::from_win32(
                    windows::Win32::Foundation::ERROR_FILE_NOT_FOUND.0,
                )
                .0
            } else {
                windows::Win32::Foundation::E_UNEXPECTED.0
            };
            return finish(
                result,
                DESKBOX_NATIVE_STATUS_OPERATION_FAILED,
                hresult,
                false,
            );
        }

        let Some(item) = restore_item else {
            return finish(
                result,
                DESKBOX_NATIVE_STATUS_OPERATION_FAILED,
                windows::Win32::Foundation::E_UNEXPECTED.0,
                false,
            );
        };
        result.attempted_phases |= DESKBOX_RECYCLE_BIN_PHASE_INVOKE;
        let verb = VARIANT::from(BSTR::from(RESTORE_VERB));
        // SAFETY: The verb VARIANT and FolderItem remain alive for the call.
        match unsafe { item.InvokeVerb(&verb) } {
            Ok(()) => {
                result.invoke_hresult = DESKBOX_NATIVE_S_OK;
                result.restored_count = 1;
            }
            Err(error) => {
                result.invoke_hresult = error.code().0;
                return finish(
                    result,
                    DESKBOX_NATIVE_STATUS_OPERATION_FAILED,
                    error.code().0,
                    false,
                );
            }
        }
    }

    finish(result, DESKBOX_NATIVE_STATUS_OK, DESKBOX_NATIVE_S_OK, true)
}

unsafe fn item_name_matches(
    item: &FolderItem,
    expected_name: &[u16],
    result: &mut DeskBoxRecycleBinResultV1,
) -> Result<bool, i32> {
    result.attempted_phases |= DESKBOX_RECYCLE_BIN_PHASE_ITEM_NAME;
    // SAFETY: The generated FolderItem owns the returned BSTR.
    let name = match unsafe { item.Name() } {
        Ok(value) => value,
        Err(error) => {
            result.item_name_hresult = error.code().0;
            return Err(error.code().0);
        }
    };

    result.item_name_hresult = DESKBOX_NATIVE_S_OK;
    // SAFETY: Both slices are readable for their declared lengths.
    Ok(unsafe { CompareStringOrdinal(&name, expected_name, true) == CSTR_EQUAL })
}

unsafe fn deleted_from_matches(
    item: &FolderItem,
    expected_parent: &[u16],
    result: &mut DeskBoxRecycleBinResultV1,
) -> Result<bool, i32> {
    result.attempted_phases |= DESKBOX_RECYCLE_BIN_PHASE_PROPERTY;
    let item2: FolderItem2 = match item.cast() {
        Ok(value) => value,
        Err(error) => {
            result.property_hresult = error.code().0;
            return Err(error.code().0);
        }
    };
    let property_name = BSTR::from(DELETED_FROM_PROPERTY);
    // SAFETY: The property BSTR remains alive for the synchronous COM call.
    let value = match unsafe { item2.ExtendedProperty(&property_name) } {
        Ok(value) => value,
        Err(error) => {
            result.property_hresult = error.code().0;
            return Err(error.code().0);
        }
    };
    if value.vt() != VT_BSTR {
        result.property_hresult = windows::Win32::Foundation::TYPE_E_TYPEMISMATCH.0;
        return Err(windows::Win32::Foundation::TYPE_E_TYPEMISMATCH.0);
    }
    let deleted_from = match BSTR::try_from(&value) {
        Ok(value) => value,
        Err(error) => {
            result.property_hresult = error.code().0;
            return Err(error.code().0);
        }
    };
    let normalized = match normalize_full_path(&deleted_from) {
        Ok(value) => value,
        Err(hresult) => {
            result.property_hresult = hresult;
            return Err(hresult);
        }
    };

    result.property_hresult = DESKBOX_NATIVE_S_OK;
    Ok(paths_equal(&normalized, expected_parent))
}

fn finish(
    result: &mut DeskBoxRecycleBinResultV1,
    status: u32,
    operation_hresult: i32,
    succeeded: bool,
) -> u32 {
    result.status = status;
    result.operation_hresult = operation_hresult;
    result.operation_succeeded = u32::from(succeeded);
    status
}
