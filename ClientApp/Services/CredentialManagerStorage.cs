using System;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Клас для роботи з Windows Credential Manager
/// Найбільш безпечний спосіб зберігання токенів в Windows
/// </summary>
public static class CredentialManagerStorage
{
    private const string CredentialTarget = "YourAppName_OAuth_Token";

    /// <summary>
    /// Зберігає токен в Windows Credential Manager
    /// </summary>
    public static void SaveToken(string token)
    {
        try
        {
            var credential = new Credential
            {
                Target = CredentialTarget,
                Type = CredentialType.Generic,
                PersistenceType = PersistenceType.LocalComputer,
                UserName = Environment.UserName,
                Secret = token
            };

            NativeMethods.CredWrite(credential);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving credential: {ex.Message}");
        }
    }

    /// <summary>
    /// Завантажує токен з Windows Credential Manager
    /// </summary>
    public static string LoadToken()
    {
        try
        {
            var credential = NativeMethods.CredRead(CredentialTarget, CredentialType.Generic);
            return credential?.Secret;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading credential: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Видаляє токен з Windows Credential Manager
    /// </summary>
    public static void DeleteToken()
    {
        try
        {
            NativeMethods.CredDelete(CredentialTarget, CredentialType.Generic);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting credential: {ex.Message}");
        }
    }

    /// <summary>
    /// Перевіряє чи існує збережений токен
    /// </summary>
    public static bool HasStoredToken()
    {
        try
        {
            var credential = NativeMethods.CredRead(CredentialTarget, CredentialType.Generic);
            return credential != null;
        }
        catch
        {
            return false;
        }
    }

    #region Native Methods and Structures

    private enum CredentialType : uint
    {
        Generic = 1,
        DomainPassword = 2,
        DomainCertificate = 3,
        DomainVisiblePassword = 4
    }

    private enum PersistenceType : uint
    {
        Session = 1,
        LocalComputer = 2,
        Enterprise = 3
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    private class Credential
    {
        public string Target { get; set; }
        public string UserName { get; set; }
        public string Secret { get; set; }
        public CredentialType Type { get; set; }
        public PersistenceType PersistenceType { get; set; }
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref NativeCredential credential, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(
            string target,
            uint type,
            uint flags,
            out IntPtr credential
        );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr credential);

        public static void CredWrite(Credential credential)
        {
            var secretBytes = Encoding.Unicode.GetBytes(credential.Secret);
            var secretPtr = Marshal.AllocHGlobal(secretBytes.Length);
            Marshal.Copy(secretBytes, 0, secretPtr, secretBytes.Length);

            var nativeCredential = new NativeCredential
            {
                Type = (uint)credential.Type,
                TargetName = credential.Target,
                UserName = credential.UserName,
                CredentialBlob = secretPtr,
                CredentialBlobSize = (uint)secretBytes.Length,
                Persist = (uint)credential.PersistenceType
            };

            try
            {
                if (!CredWrite(ref nativeCredential, 0))
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(secretPtr);
            }
        }

        public static Credential CredRead(string target, CredentialType type)
        {
            IntPtr credPtr;
            if (!CredRead(target, (uint)type, 0, out credPtr))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1168) // ERROR_NOT_FOUND
                    return null;
                throw new System.ComponentModel.Win32Exception(error);
            }

            try
            {
                var nativeCredential = Marshal.PtrToStructure<NativeCredential>(credPtr);
                var secret = Marshal.PtrToStringUni(
                    nativeCredential.CredentialBlob,
                    (int)nativeCredential.CredentialBlobSize / 2
                );

                return new Credential
                {
                    Target = nativeCredential.TargetName,
                    UserName = nativeCredential.UserName,
                    Secret = secret,
                    Type = (CredentialType)nativeCredential.Type,
                    PersistenceType = (PersistenceType)nativeCredential.Persist
                };
            }
            finally
            {
                CredFree(credPtr);
            }
        }

        public static void CredDelete(string target, CredentialType type)
        {
            if (!CredDelete(target, (uint)type, 0))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 1168) // Ignore ERROR_NOT_FOUND
                {
                    throw new System.ComponentModel.Win32Exception(error);
                }
            }
        }
    }

    #endregion
}