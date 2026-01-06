using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ClientApp.Services
{
    public class FolderPickerDialog
    {
        public string SelectedPath { get; private set; }
        public string Title { get; set; } = "Select Folder";

        public bool? ShowDialog(Window owner = null)
        {
            var dialog = new OpenFolderDialog();
            var result = dialog.ShowDialog(owner);

            if (result == true)
            {
                SelectedPath = dialog.SelectedPath;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Native Windows Folder Browser Dialog
    /// </summary>
    internal class OpenFolderDialog
    {
        public string SelectedPath { get; private set; }

        public bool ShowDialog(Window owner = null)
        {
            var frm = (IFileDialog)new FileOpenDialog();

            frm.GetOptions(out uint options);
            options |= FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM;
            frm.SetOptions(options);

            if (owner != null)
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(owner);
                frm.Show(helper.Handle);
            }
            else
            {
                frm.Show(IntPtr.Zero);
            }

            if (frm.GetResult(out IShellItem item) == 0)
            {
                item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr path);
                if (path != IntPtr.Zero)
                {
                    try
                    {
                        SelectedPath = Marshal.PtrToStringAuto(path);
                        return true;
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(path);
                    }
                }
            }

            return false;
        }

        #region COM Interop

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }

        [ComImport]
        [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig]
            uint Show(IntPtr parent);

            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

            [PreserveSig]
            uint GetResult(out IShellItem ppsi);

            void AddPlace(IShellItem psi, int alignment);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        #endregion
    }
}
