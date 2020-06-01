Option Explicit On

Imports System.Configuration
Imports EPDM.Interop.epdm
Imports EPDMlib

Module Module1

    Const EXIT_SUCCESS = 0
    Const EXIT_FAILURE = 1
    Const EXIT_SKIPPED = 2
    Const UsageString = "Usage: PdmRevertFile.exe <FILE_PATH> <VERSION_NO> <CHECKIN_COMMENT> [CHECK_VERSION_NO]"
    Dim PDM_UTIL_DEBUG = False

    Function Main(args() As String) As Integer

        'example arguments
        'args(0) = "C:\Vault\Folder1\Folder1\filename.sldasm"
        'args(1) = "32"
        'args(2) = "fix: file reverted to version 32 (FDT-1155)"


        'Check to see if we should turn on debug logging
        Dim debugEnv As String = Environment.GetEnvironmentVariable("PDM_UTIL_DEBUG")
        Dim debugApp As String = ConfigurationManager.AppSettings.Item("PDM_UTIL_DEBUG")
        If debugEnv = "TRUE" Or (debugEnv = Nothing And debugApp = "TRUE") Then
            PDM_UTIL_DEBUG = True
            LogDebug("Turning on debug Logging")
        End If


        Dim NL = Environment.NewLine

        Dim filename As String
        Dim revNo As Integer
        Dim fullPath As String
        Dim checkinMsg As String
        Dim checkRevNo As Integer = 0

        LogDebug("Trying to retrieve command line arguments")
        Try
            If args.Length < 3 Or args.Length > 4 Then
                Throw New ArgumentException("Invalid number of arguments")
            End If
            fullPath = args(0)
            revNo = Convert.ToInt32(args(1))
            checkinMsg = args(2)
            filename = New IO.FileInfo(fullPath).Name
            If args.Length >= 4 Then
                checkRevNo = Convert.ToInt32(args(3))
            End If
        Catch ex As Exception
            LogError("error reading commandline args=(" + String.Join(", ", args) + ")", ex)
            LogError(NL + NL + UsageString)
            Return EXIT_FAILURE
        End Try


        'Get name of the vault in which the file is located 
        LogDebug("Trying to get name of the vault in which the file is located")
        Dim VaultName As String
        Dim vault As New EdmVault5
        Try

            VaultName = vault.GetVaultNameFromPath(fullPath)
        Catch ex As Exception
            LogError("problem retrieving vault name from path FILE_PATH=" + fullPath, ex)
            Return EXIT_FAILURE
        End Try

        'Log into the vault 
        LogDebug("Trying to log into the vault")
        Try
            vault.LoginAuto(VaultName, 0)

        Catch ex As Exception
            LogError("problem logging in VAULT_NAME=" + VaultName, ex)
            Return EXIT_FAILURE
        End Try

        Dim file As IEdmFile5
        Dim folder As IEdmFolder5 = Nothing

        'Get the interface to the file and its parent folder
        LogDebug("Trying to get the interface to the file and its parent folder")
        Try
            file = vault.GetFileFromPath(fullPath, folder)
        Catch ex As Exception
            LogError("could not get file from path FILE_PATH=" + fullPath, ex)
            Return EXIT_FAILURE
        End Try

        If file Is Nothing Then
            LogError("The file is not in the vault FILE_PATH=" + fullPath)
            Return EXIT_FAILURE
        End If

        'Check if comment is the same and if so, assume this file has already been processed so bail
        LogDebug("Trying to perform pre-work checks")
        Try
            Dim curVer = file.CurrentVersion
            Dim enumVer = DirectCast(file, IEdmEnumeratorVersion5)
            If enumVer.GetVersion(curVer).Comment = checkinMsg Then
                LogError("Comment already present so doing nothing, FILE_PATH=" + fullPath + ", VERSION_NO=" + revNo.ToString() + ", CHECKIN_MESSAGE=" + checkinMsg)
                Return EXIT_SKIPPED
            End If
            If checkRevNo <> 0 And checkRevNo <> curVer Then
                LogError("Check version does not match so skipping, FILE_PATH=" + fullPath + ", CURRENT_VERSION_NO=" + curVer.ToString() + ", CHECK_VERSION_NO=" + checkRevNo.ToString())
                Return EXIT_SKIPPED
            End If
        Catch ex As Exception
            LogError("could not check version message FILE_PATH=" + fullPath, ex)
            Return EXIT_FAILURE
        End Try

        Dim didLock As Boolean = False
        'Check out the file 
        LogDebug("Trying to check out file")
        Try
            If Not file.IsLocked Then
                file.LockFile(folder.ID, 0, EdmLockFlag.EdmLock_Simple)
                didLock = True
            End If
        Catch ex As Exception
            LogError("could not lock file FILE_PATH=" + fullPath, ex)
            Return EXIT_FAILURE
        End Try

        'Get the specific version
        LogDebug("Trying to retrieve requested file version")
        Try
            'IMPORTANT: must use EdmGet_Simple to avoid getting read-only file, otherwise will have error during check-in
            file.GetFileCopy(0, revNo, folder.ID, EdmGetFlag.EdmGet_Simple)
        Catch ex As Exception
            LogError("could not get file FILE_PATH=" + fullPath + ", VERSION_NO=" + revNo.ToString(), ex)
            Return EXIT_FAILURE
        End Try

        'Check-in the file
        LogDebug("Trying to check in the file")
        Try
            Dim unlockFlags = EdmUnlockFlag.EdmUnlock_IgnoreRefsOutsideVault
            If WaitFileUnlock(fullPath, 10000) Then
                file.UnlockFile(0, checkinMsg, unlockFlags)
            Else
                Throw New Exception("Gave up waiting for file lock")
            End If
        Catch ex As Exception
            LogError("cannot check-in file FILE_PATH=" + fullPath, ex)
            If didLock Then
                file.UndoLockFile(0)
            End If
            Return EXIT_FAILURE
        End Try

        LogInfo("yay! FILE_PATH=" + fullPath)
        Return EXIT_SUCCESS
    End Function

    Function WaitFileUnlock(path As String, waitTimeMilliseconds As Int32) As Boolean
        Const sleepTime = 500
        Dim maxRetries = Math.Ceiling(waitTimeMilliseconds * 1.0 / sleepTime)
        For i As Int32 = 1 To maxRetries
            Try
                LogDebug("Trying to get exclusive access to file")
                Dim fs = New System.IO.FileStream(path, IO.FileMode.Open, IO.FileAccess.ReadWrite)
                fs.Close()
                Return True
            Catch ex As Exception
                ' do nothing, just try again
                LogDebug("Failed to retreive access, waiting for a bit")
                Threading.Thread.Sleep(sleepTime)
            End Try

        Next

        Return False
    End Function

    Sub LogError(Message As String, Optional ex As Exception = Nothing)
        Dim NL = Environment.NewLine

        If ex Is Nothing Then
            Console.Error.WriteLine(_GetLogMessage("ERROR", Message))
        Else
            Console.Error.WriteLine(_GetLogMessage("INFO", Message + NL + NL + ex.ToString()))
        End If
    End Sub

    Sub LogDebug(Message As String)
        If PDM_UTIL_DEBUG Then
            Console.WriteLine(_GetLogMessage("DEBUG", Message))
        End If
    End Sub

    Sub LogInfo(Message As String)
        Console.WriteLine(_GetLogMessage("INFO", Message))
    End Sub

    Function _GetLogMessage(LogType As String, Message As String) As String
        Dim format = "yyyy-MM-dd'T'HH:mm:ss.ffffffK"
        Dim timestamp = Date.Now.ToString(format)
        Return timestamp + " :: " + LogType + " :: " + Message
    End Function

End Module
