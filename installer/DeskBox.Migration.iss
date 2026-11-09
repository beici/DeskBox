[Code]
// The cleanup script is extracted to the user-writable {tmp}, so pin its hash
// at compile time and execute only the verified in-memory copy below.
#if DeskBoxBundledRuntime
#define DeskBoxCleanupScriptSha256 GetSHA256OfFile('..\scripts\cleanup-deskbox-install.ps1')
#endif
const
  DeskBoxAppCompatLayersKey = 'Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers';
#if DeskBoxBundledRuntime
  DeskBoxCleanupScriptSha256 = '{#DeskBoxCleanupScriptSha256}';
  DeskBoxBase64Alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
#endif

function PrepareDeskBoxDependencies(var NeedsRestart: Boolean): string; forward;

#if DeskBoxBundledRuntime
function QuoteDeskBoxPowerShellLiteral(Value: string): string;
var
  Escaped: string;
begin
  Escaped := Value;
  StringChangeEx(Escaped, '''', '''' + '''', True);
  Result := '''' + Escaped + '''';
end;

function EncodeDeskBoxBase64Utf16Le(Value: string): string;
var
  I, CodeUnit, Buffer, Bits: Integer;
begin
  Result := '';
  Buffer := 0;
  Bits := 0;
  for I := 1 to Length(Value) do
  begin
    CodeUnit := Ord(Value[I]);
    Buffer := (Buffer shl 8) or (CodeUnit and $FF);
    Bits := Bits + 8;
    Buffer := (Buffer shl 8) or (CodeUnit shr 8);
    Bits := Bits + 8;
    while Bits >= 6 do
    begin
      Bits := Bits - 6;
      Result := Result + Copy(DeskBoxBase64Alphabet, ((Buffer shr Bits) and $3F) + 1, 1);
    end;
    Buffer := Buffer and ((1 shl Bits) - 1);
  end;
  if Bits > 0 then
    Result := Result + Copy(DeskBoxBase64Alphabet, ((Buffer shl (6 - Bits)) and $3F) + 1, 1);
  while (Length(Result) mod 4) <> 0 do
    Result := Result + '=';
end;

function BuildDeskBoxCleanupCommand(ScriptText: string; Arguments: string): string;
begin
  Result := '& {' + Chr(13) + Chr(10) + ScriptText + Chr(13) + Chr(10) + '} ' + Arguments;
  Result := EncodeDeskBoxBase64Utf16Le(Result);
end;

procedure CleanupDeskBoxInstall;
var
  PowerShellPath: string;
  CleanupScriptPath: string;
  CleanupScriptBytes: AnsiString;
  CleanupScriptHash: string;
  CleanupScriptText: string;
  CleanupArguments: string;
  CurrentManifestPath: string;
  LegacyManifestPath: string;
  PreviousManifestPath: string;
  Parameters: string;
  ResultCode: Integer;
begin
  if not DirectInstallUpgrade then
  begin
    Log('DeskBox first-install path detected; stale install cleanup skipped.');
    Exit;
  end;

  // Called from CurStepChanged(ssInstall), so the new manifest is extracted
  // here instead of arriving with the install pass: the stale-file comparison
  // must complete before Setup writes any new payload file.
  ExtractTemporaryFile('cleanup-deskbox-install.ps1');
  ExtractTemporaryFile('DeskBox.LegacyBundledRuntimeFiles.txt');
  ExtractTemporaryFile('DeskBox.InstallManifest.txt');

  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  CleanupScriptPath := ExpandConstant('{tmp}\cleanup-deskbox-install.ps1');
  CurrentManifestPath := ExpandConstant('{tmp}\DeskBox.InstallManifest.txt');
  LegacyManifestPath := ExpandConstant('{tmp}\DeskBox.LegacyBundledRuntimeFiles.txt');
  PreviousManifestPath := AddBackslash(WizardDirValue) + 'DeskBox.InstallManifest.txt';

  // Read the extracted script exactly once. The bytes that are hashed here are
  // the same bytes handed to PowerShell, so replacing the temporary file
  // cannot change what runs after the check.
  if not LoadStringFromFile(CleanupScriptPath, CleanupScriptBytes) then
    RaiseException('DeskBox could not read the install cleanup script.');

  CleanupScriptHash := GetSHA256OfString(CleanupScriptBytes);
  if CompareText(CleanupScriptHash, DeskBoxCleanupScriptSha256) <> 0 then
  begin
    Log('DeskBox install cleanup script integrity check failed: expected ' +
      DeskBoxCleanupScriptSha256 + ', got ' + CleanupScriptHash + '.');
    RaiseException('DeskBox install cleanup script failed its SHA-256 integrity check; the installation was aborted.');
  end;

  CleanupScriptText := UTF8Decode(CleanupScriptBytes);

  CleanupArguments :=
    ' -InstallRoot ' + QuoteDeskBoxPowerShellLiteral(WizardDirValue) +
    ' -CurrentManifestPath ' + QuoteDeskBoxPowerShellLiteral(CurrentManifestPath) +
    ' -LegacyManifestPath ' + QuoteDeskBoxPowerShellLiteral(LegacyManifestPath);
  // Only ask for an exact previous-file cleanup when the previous payload
  // actually left a manifest behind; otherwise the script falls back to the
  // legacy compatibility manifest.
  if FileExists(PreviousManifestPath) then
    CleanupArguments := CleanupArguments +
      ' -PreviousManifestPath ' + QuoteDeskBoxPowerShellLiteral(PreviousManifestPath)
  else
    Log('DeskBox previous install manifest not found; cleanup uses the legacy compatibility manifest only.');

  // -EncodedCommand cannot take script arguments, so the verified script text
  // is wrapped in a script block and the parameter suffix is appended inside
  // the encoded command. The suffix is Setup-owned constant text.
  Parameters :=
    '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ' +
    BuildDeskBoxCleanupCommand(CleanupScriptText, CleanupArguments);

  if not Exec(PowerShellPath, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException('DeskBox could not start the install cleanup process.');

  if ResultCode <> 0 then
    RaiseException(Format('DeskBox install cleanup failed with exit code %d.', [ResultCode]));

  Log('DeskBox stale install-file cleanup completed successfully.');
end;
#endif

procedure DeleteAppCompatLayerValue(RootKey: Integer; ExePath: string);
var
  Value: string;
begin
  if ExePath = '' then
    Exit;

  if RegQueryStringValue(RootKey, DeskBoxAppCompatLayersKey, ExePath, Value) and
     (Pos('RUNASADMIN', Uppercase(Value)) > 0) then
  begin
    if RegDeleteValue(RootKey, DeskBoxAppCompatLayersKey, ExePath) then
      Log('DeskBox installer removed AppCompat RUNASADMIN value: ' + ExePath)
    else
      Log('DeskBox installer could not remove AppCompat value: ' + ExePath);
  end;
end;

procedure CleanupInstallAppCompatFlags(InstallPath: string);
var
  ExePath: string;
begin
  ExePath := AddBackslash(NormalizeDirPath(InstallPath)) + DeskBoxLegacyExeName;
  DeleteAppCompatLayerValue(HKEY_CURRENT_USER, ExePath);

  if IsAdminInstallMode then
    DeleteAppCompatLayerValue(HKEY_LOCAL_MACHINE, ExePath);
end;

function InitializeSetup: Boolean;
begin
  // Program Files is now the supported all-users location. Older installations
  // found there are upgraded in place instead of being treated as disposable
  // legacy copies.
  Result := PrepareDirectInstallPlan;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := DirectInstallUpgrade and (PageID = wpSelectDir);
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
var
  DependencyError: string;
begin
  Result := '';

  // Close only the DeskBox process that belongs to the install being updated.
  // Restart Manager remains the final file-lock fallback for Setup itself.
  if not StopDeskBoxProcessesAtPath(WizardDirValue) then
    Log('DeskBox path-scoped process shutdown failed; Setup will continue with Restart Manager handling file locks.');

  // Give the process time to fully exit before Restart Manager runs.
  Sleep(2000);
  Log('DeskBox process termination completed.');

  // DeskBox must continue to run at normal user integrity so Explorer drag and
  // drop remains available after an upgrade from older Program Files builds.
  CleanupInstallAppCompatFlags(WizardDirValue);

  DependencyError := PrepareDeskBoxDependencies(NeedsRestart);
  if DependencyError <> '' then
    Result := DependencyError;
end;
