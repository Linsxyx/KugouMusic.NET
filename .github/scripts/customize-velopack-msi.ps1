[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$AppFolderName = 'KugouAvaloniaPlayer'

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'MSI customization requires Windows.'
}

$msiPath = (Resolve-Path -LiteralPath $Path).Path
if ([IO.Path]::GetExtension($msiPath) -ne '.msi') {
    throw "Expected an .msi file: $msiPath"
}

function Invoke-ComMethod {
    param(
        [Parameter(Mandatory = $true)] $Target,
        [Parameter(Mandatory = $true)] [string] $Name,
        [AllowNull()] [object[]] $Arguments
    )

    return $Target.GetType().InvokeMember(
        $Name,
        [Reflection.BindingFlags]::InvokeMethod,
        $null,
        $Target,
        $Arguments
    )
}

function Invoke-MsiSql {
    param(
        [Parameter(Mandatory = $true)] $Database,
        [Parameter(Mandatory = $true)] [string] $Sql
    )

    $view = Invoke-ComMethod -Target $Database -Name 'OpenView' -Arguments @($Sql)
    try {
        [void](Invoke-ComMethod -Target $view -Name 'Execute' -Arguments $null)
    }
    catch {
        throw "Failed to execute MSI SQL: $Sql`n$($_.Exception.Message)"
    }
    finally {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

function Test-MsiDialogExists {
    param(
        [Parameter(Mandatory = $true)] $Database,
        [Parameter(Mandatory = $true)] [string] $Dialog
    )

    $sql = "SELECT ``Dialog`` FROM ``Dialog`` WHERE ``Dialog``='$Dialog'"
    $view = Invoke-ComMethod -Target $Database -Name 'OpenView' -Arguments @($sql)
    $record = $null

    try {
        [void](Invoke-ComMethod -Target $view -Name 'Execute' -Arguments $null)
        $record = Invoke-ComMethod -Target $view -Name 'Fetch' -Arguments $null
        return $null -ne $record
    }
    finally {
        if ($null -ne $record) {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

function Assert-MsiPerMachineElevationPolicy {
    param(
        [Parameter(Mandatory = $true)] $Database
    )

    $sql = "SELECT ``Property`` FROM ``Property`` WHERE ``Property``='ALLUSERS' AND ``Value``='1'"
    $view = Invoke-ComMethod -Target $Database -Name 'OpenView' -Arguments @($sql)
    $record = $null
    try {
        [void](Invoke-ComMethod -Target $view -Name 'Execute' -Arguments $null)
        $record = Invoke-ComMethod -Target $view -Name 'Fetch' -Arguments $null
        if ($null -eq $record) {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
            $view = Invoke-ComMethod -Target $Database -Name 'OpenView' -Arguments @(
                "SELECT ``Dialog_`` FROM ``ControlEvent`` WHERE ``Dialog_``='InstallScopeDlg' AND ``Control_``='Next' AND ``Event``='[ALLUSERS]' AND ``Argument``='1' AND ``Ordering`` < 8")
            [void](Invoke-ComMethod -Target $view -Name 'Execute' -Arguments $null)
            $record = Invoke-ComMethod -Target $view -Name 'Fetch' -Arguments $null
        }
        if ($null -eq $record) {
            throw 'Velopack MSI is not configured as a per-machine installer (ALLUSERS=1); it would not reliably request elevation.'
        }
    }
    finally {
        if ($null -ne $record) {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null

try {
    # msiOpenDatabaseModeTransact = 1
    $database = Invoke-ComMethod -Target $installer -Name 'OpenDatabase' -Arguments @($msiPath, 1)
    Assert-MsiPerMachineElevationPolicy -Database $database

    if (Test-MsiDialogExists -Database $database -Dialog 'InstallDirDlg') {
        Write-Host "MSI already contains InstallDirDlg: $msiPath"
        return
    }

    $actionName = 'SetKugouAvaloniaPlayerInstallFolder'
    $blockedInstallLocationCondition = 'INSTALLFOLDER ~>> "[ProgramFilesFolder]" OR INSTALLFOLDER ~>> "[ProgramFiles64Folder]" OR INSTALLFOLDER ~>> "[ProgramFiles6432Folder]" OR INSTALLFOLDER ~>> "[WindowsFolder]" OR INSTALLFOLDER ~>> "[SystemFolder]"'

    $statements = @(
        'INSERT INTO `Dialog` (`Dialog`, `HCentering`, `VCentering`, `Width`, `Height`, `Attributes`, `Title`, `Control_First`, `Control_Default`, `Control_Cancel`) VALUES (''InstallDirDlg'', 50, 50, 370, 270, 7, ''[MsiDlgTitle]'', ''InstallDirEdit'', ''Next'', ''Cancel'')',
        'INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next`, `Help`) VALUES (''InstallDirDlg'', ''BannerBitmap'', ''Bitmap'', 0, 0, 370, 44, 1, '''', ''WixUI_Bmp_Banner'', '''', '''')',
        'INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next`, `Help`) VALUES (''InstallDirDlg'', ''BannerLine'', ''Line'', 0, 44, 370, 0, 1, '''', '''', '''', '''')',
        'INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next`, `Help`) VALUES (''InstallDirDlg'', ''BottomLine'', ''Line'', 0, 234, 370, 0, 1, '''', '''', '''', '''')',
        'INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next`, `Help`) VALUES (''InstallDirDlg'', ''Title'', ''Text'', 15, 6, 200, 15, 196611, '''', ''[MsiBrowseTitle]'', '''', '''')',
        'INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next`, `Help`) VALUES (''InstallDirDlg'', ''Description'', ''Text'', 25, 23, 280, 15, 196611, '''', ''[MsiBrowseDescription]'', '''', '''')',
        'INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next`, `Help`) VALUES (''InstallDirDlg'', ''PathLabel'', ''Text'', 25, 70, 320, 10, 3, '''', ''[MsiBrowsePathLabel]'', '''', '''')',
        'INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next`, `Help`) VALUES (''InstallDirDlg'', ''InstallDirEdit'', ''PathEdit'', 25, 84, 264, 18, 11, ''WIXUI_INSTALLDIR'', '''', ''Browse'', '''')',
        'INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next`, `Help`) VALUES (''InstallDirDlg'', ''Browse'', ''PushButton'', 296, 84, 56, 18, 3, '''', ''[MsiReadyBtnChange]'', ''Back'', '''')',
        'INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next`, `Help`) VALUES (''InstallDirDlg'', ''Back'', ''PushButton'', 180, 243, 56, 17, 3, '''', ''[MsiBtnBack]'', ''Next'', '''')',
        'INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next`, `Help`) VALUES (''InstallDirDlg'', ''Next'', ''PushButton'', 236, 243, 56, 17, 3, '''', ''[MsiBtnNext]'', ''Cancel'', '''')',
        'INSERT INTO `Control` (`Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next`, `Help`) VALUES (''InstallDirDlg'', ''Cancel'', ''PushButton'', 304, 243, 56, 17, 3, '''', ''[MsiBtnCancel]'', ''InstallDirEdit'', '''')',
        'INSERT INTO `ControlEvent` (`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering`) VALUES (''InstallDirDlg'', ''Browse'', ''[_BrowseProperty]'', ''[WIXUI_INSTALLDIR]'', ''1'', 1)',
        'INSERT INTO `ControlEvent` (`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering`) VALUES (''InstallDirDlg'', ''Browse'', ''NewDialog'', ''BrowseDlg'', ''1'', 2)',
        'INSERT INTO `ControlEvent` (`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering`) VALUES (''InstallDirDlg'', ''Back'', ''NewDialog'', ''WelcomeDlg'', ''1'', 1)',
        "INSERT INTO ``CustomAction`` (``Action``, ``Type``, ``Source``, ``Target``) VALUES ('$actionName', 51, 'INSTALLFOLDER', '[INSTALLFOLDER]\$AppFolderName')",
        'INSERT INTO `ControlEvent` (`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering`) VALUES (''InstallDirDlg'', ''Next'', ''SetTargetPath'', ''[WIXUI_INSTALLDIR]'', ''1'', 1)',
        "INSERT INTO ``ControlEvent`` (``Dialog_``, ``Control_``, ``Event``, ``Argument``, ``Condition``, ``Ordering``) VALUES ('InstallDirDlg', 'Next', 'DoAction', '$actionName', 'NOT (INSTALLFOLDER ~>> ""\$AppFolderName"" OR INSTALLFOLDER ~>> ""\$AppFolderName\\"")', 2)",
        "INSERT INTO ``ControlEvent`` (``Dialog_``, ``Control_``, ``Event``, ``Argument``, ``Condition``, ``Ordering``) VALUES ('InstallDirDlg', 'Next', 'SpawnDialog', 'InvalidDirDlg', '$blockedInstallLocationCondition', 3)",
        "INSERT INTO ``ControlEvent`` (``Dialog_``, ``Control_``, ``Event``, ``Argument``, ``Condition``, ``Ordering``) VALUES ('InstallDirDlg', 'Next', 'NewDialog', 'VerifyReadyDlg', 'NOT ($blockedInstallLocationCondition)', 4)",
        'INSERT INTO `ControlEvent` (`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering`) VALUES (''InstallDirDlg'', ''Cancel'', ''SpawnDialog'', ''CancelDlg'', ''1'', 1)',
        'DELETE FROM `ControlEvent` WHERE `Dialog_`=''BrowseDlg'' AND `Control_`=''OK'' AND `Event`=''DoAction'' AND `Argument`=''RustValidatePath''',
        'DELETE FROM `ControlEvent` WHERE `Dialog_`=''BrowseDlg'' AND `Control_`=''OK'' AND `Event`=''SpawnDialog'' AND `Argument`=''InvalidDirDlg''',
        'DELETE FROM `ControlEvent` WHERE `Dialog_`=''BrowseDlg'' AND `Control_`=''OK'' AND `Event`=''EndDialog'' AND `Argument`=''Return''',
        'INSERT INTO `ControlEvent` (`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering`) VALUES (''BrowseDlg'', ''OK'', ''NewDialog'', ''InstallDirDlg'', ''1'', 2)',
        'DELETE FROM `ControlEvent` WHERE `Dialog_`=''BrowseDlg'' AND `Control_`=''Cancel'' AND `Event`=''EndDialog'' AND `Argument`=''Return''',
        'INSERT INTO `ControlEvent` (`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering`) VALUES (''BrowseDlg'', ''Cancel'', ''NewDialog'', ''InstallDirDlg'', ''1'', 2)',
        'DELETE FROM `ControlEvent` WHERE `Dialog_`=''WelcomeDlg'' AND `Control_`=''Next'' AND `Event`=''NewDialog'' AND `Argument`=''VerifyReadyDlg'' AND `Condition`=''NOT Installed''',
        'INSERT INTO `ControlEvent` (`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering`) VALUES (''WelcomeDlg'', ''Next'', ''NewDialog'', ''InstallDirDlg'', ''NOT Installed'', 3)',
        'DELETE FROM `ControlEvent` WHERE `Dialog_`=''VerifyReadyDlg'' AND `Control_`=''Back'' AND `Event`=''NewDialog'' AND `Argument`=''InstallScopeDlg''',
        'INSERT INTO `ControlEvent` (`Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering`) VALUES (''VerifyReadyDlg'', ''Back'', ''NewDialog'', ''InstallDirDlg'', ''NOT Installed'', 1)'
    )

    foreach ($statement in $statements) {
        Invoke-MsiSql -Database $database -Sql $statement
    }

    [void](Invoke-ComMethod -Target $database -Name 'Commit' -Arguments $null)
    Write-Host "Added install directory selection to: $msiPath"
}
finally {
    if ($null -ne $database) {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
