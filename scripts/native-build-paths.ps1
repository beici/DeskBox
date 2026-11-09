function Get-DeskBoxNativePrivacyFlags {
    param(
        [Parameter(Mandatory)][ValidateSet('Debug', 'Release')][string]$Configuration,
        [Parameter(Mandatory)][string]$RepositoryRoot
    )

    if ($Configuration -ne 'Release') {
        return
    }

    # Rust embeds panic locations and the linker embeds a PDB lookup path.
    # Keep useful relative locations without publishing the builder's profile.
    '-C'
    'link-arg=/PDBALTPATH:%_PDB%'
    $userProfileDirectory = [Environment]::GetFolderPath('UserProfile')
    $roots = @(
        @($userProfileDirectory, '/_/user'),
        @($(if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $userProfileDirectory '.cargo' }), '/_/cargo'),
        @($(if ($env:RUSTUP_HOME) { $env:RUSTUP_HOME } else { Join-Path $userProfileDirectory '.rustup' }), '/_/rustup'),
        @([System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/'), '/_/DeskBox')
    )
    foreach ($mapping in $roots) {
        if (-not [string]::IsNullOrWhiteSpace($mapping[0])) {
            foreach ($prefix in @($mapping[0], $mapping[0].Replace('\', '/')) | Select-Object -Unique) {
                "--remap-path-prefix=$prefix=$($mapping[1])"
            }
        }
    }
}
