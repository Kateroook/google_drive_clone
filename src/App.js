import React, { useState, useEffect } from 'react';
import { ThemeProvider, createTheme, CssBaseline, AppBar, Toolbar, Typography, Box, Button, IconButton, Drawer,
  List, ListItem, ListItemIcon, ListItemText, ListItemButton,
  Card, CardContent, 
  Table, TableBody, TableCell, TableContainer, TableHead, TableRow,
  Paper, Dialog, DialogTitle, DialogContent, DialogActions, TextField, 
  Menu, MenuItem, Chip, Avatar, Breadcrumbs, Link,
  CircularProgress, Tooltip,
  FormGroup, FormControlLabel, FormControl,
  Checkbox, Select,
  InputLabel, Stack, Divider, Alert, Snackbar
} from '@mui/material';
import {
  Upload as UploadIcon,
  Download as DownloadIcon,
  Delete as DeleteIcon,
  Visibility as VisibilityIcon,
  Folder as FolderIcon,
  InsertDriveFile as FileIcon,
  Add as AddIcon,
  Home as HomeIcon,
  Logout as LogoutIcon,
  Refresh as RefreshIcon,
  Sort as SortIcon,
  FilterList as FilterListIcon,
  Settings as SettingsIcon,
  Close as CloseIcon,
  MoreVert as MoreVertIcon,
  CloudUpload as CloudUploadIcon
} from '@mui/icons-material';

const API_URL = 'http://localhost:5000';
const CLIENT_TYPE = 'web';
const REDIRECT_URI = window.location.origin + '/callback';

const theme = createTheme({
  palette: {
    primary: {
      main: '#1976d2',
      light: '#42a5f5',
      dark: '#1565c0',
    },
    secondary: {
      main: '#9c27b0',
    },
    background: {
      default: '#f5f5f5',
      paper: '#ffffff',
    },
  },
  shape: {
    borderRadius: 8,
  },
  components: {
    MuiButton: {
      styleOverrides: {
        root: {
          textTransform: 'none',
          fontWeight: 500,
        },
      },
    },
  },
});

// Utility functions
const formatDate = (dateString) => {
  if (!dateString) return 'N/A';
  const date = new Date(dateString);
  return date.toLocaleString('uk-UA', { 
    year: 'numeric', 
    month: '2-digit', 
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  });
};

const formatFileSize = (bytes) => {
  if (!bytes) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i];
};

const getFileExtension = (filename) => {
  return filename.split('.').pop().toLowerCase();
};

// PKCE Helper Functions
const base64URLEncode = (buffer) => {
  return btoa(String.fromCharCode(...new Uint8Array(buffer)))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=/g, '');
};

const generateRandomBytes = (length) => {
  return window.crypto.getRandomValues(new Uint8Array(length));
};

const sha256 = async (plain) => {
  const encoder = new TextEncoder();
  const data = encoder.encode(plain);
  return await window.crypto.subtle.digest('SHA-256', data);
};

const generateCodeChallenge = async (codeVerifier) => {
  const hashed = await sha256(codeVerifier);
  return base64URLEncode(hashed);
};

export default function GoogleDriveClone() {
  const [token, setToken] = useState('');
  const [user, setUser] = useState(null);
  const [files, setFiles] = useState([]);
  const [folders, setFolders] = useState([]);
  const [allFolders, setAllFolders] = useState([]);
  const [currentFolder, setCurrentFolder] = useState(null);
  const [breadcrumbPath, setBreadcrumbPath] = useState([]);
  const [selectedFile, setSelectedFile] = useState(null);
  const [fileContent, setFileContent] = useState('');
  const [loading, setLoading] = useState(false);
  const [showNewFolderDialog, setShowNewFolderDialog] = useState(false);
  const [newFolderName, setNewFolderName] = useState('');
  const [sortBy, setSortBy] = useState('name');
  const [sortOrder, setSortOrder] = useState('asc');
  const [filterType, setFilterType] = useState('all');
  const [showColumnSettings, setShowColumnSettings] = useState(false);
  const [visibleColumns, setVisibleColumns] = useState({
    name: true,
    size: true,
    created: true,
    updated: true,
    uploadedBy: true,
    editedBy: true,
    type: true
  });
  const [isAuthenticating, setIsAuthenticating] = useState(false);
  const [confirmDialog, setConfirmDialog] = useState({ open: false, title: '', message: '', onConfirm: null });
  const [anchorEl, setAnchorEl] = useState(null);
  const [selectedFolder, setSelectedFolder] = useState(null);
  const [snackbar, setSnackbar] = useState({ open: false, message: '', severity: 'success' });
  const [isDragging, setIsDragging] = useState(false);

  const showSnackbar = (message, severity = 'success') => {
    setSnackbar({ open: true, message, severity });
  };

  // Функція для побудови шляху breadcrumbs
  const buildBreadcrumbPath = (folderId) => {
    if (!folderId) {
      setBreadcrumbPath([]);
      return;
    }

    const path = [];
    let currentId = folderId;

    while (currentId) {
      const folder = allFolders.find(f => f.id === currentId);
      if (folder) {
        path.unshift(folder);
        currentId = folder.parent_id;
      } else {
        break;
      }
    }

    setBreadcrumbPath(path);
  };

  // Перевірка чи це callback сторінка
  useEffect(() => {
    const path = window.location.pathname;
    if (path === '/callback' || path === '/callback/') {
      handleCallbackPage();
    }
  }, []);

  // Завантаження токену з localStorage
  useEffect(() => {
    const savedToken = localStorage.getItem('oauth_access_token');
    if (savedToken) {
      setToken(savedToken);
      console.log('✅ Token loaded from localStorage');
    }
  }, []);

  useEffect(() => {
    if (token) {
      fetchUserInfo();
      fetchFolders();
      fetchAllFolders();
      fetchFiles();
    }
  }, [token, currentFolder]);

  // Оновлення breadcrumbs при зміні папки або списку всіх папок
  useEffect(() => {
    if (currentFolder) {
      buildBreadcrumbPath(currentFolder.id);
    } else {
      setBreadcrumbPath([]);
    }
  }, [currentFolder, allFolders]);

  const handleCallbackPage = async () => {
    console.log('\n=== React Web Client - OAuth Callback ===');
    setIsAuthenticating(true);

    const urlParams = new URLSearchParams(window.location.search);
    const code = urlParams.get('code');
    const state = urlParams.get('state');
    const error = urlParams.get('error');

    console.log('Callback parameters:');
    console.log('  Code:', code?.substring(0, 10) + '...');
    console.log('  State:', state);
    console.log('  Error:', error);

    if (error) {
      console.error('❌ OAuth error:', error);
      showSnackbar('Помилка авторизації: ' + error, 'error');
      setTimeout(() => {
        window.location.href = '/';
      }, 2000);
      return;
    }

    if (!code || !state) {
      console.error('❌ Missing code or state');
      showSnackbar('Невірні параметри callback', 'error');
      setTimeout(() => {
        window.location.href = '/';
      }, 2000);
      return;
    }

    try {
      // Отримання збережених PKCE параметрів
      const codeVerifier = sessionStorage.getItem('oauth_code_verifier');
      const storedState = sessionStorage.getItem('oauth_state');

      console.log('Stored PKCE data:');
      console.log('  Code Verifier:', codeVerifier?.substring(0, 10) + '...');
      console.log('  Stored State:', storedState);

      if (!codeVerifier || !storedState) {
        throw new Error('PKCE data not found in session storage');
      }

      if (storedState !== state) {
        console.error('❌ State mismatch!');
        console.error('  Expected:', storedState);
        console.error('  Received:', state);
        throw new Error('State mismatch - possible CSRF attack');
      }

      console.log('✅ State verified successfully');

      // Обмін коду на токен
      console.log('Exchanging code for token...');
      const response = await fetch(`${API_URL}/auth/token`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          code,
          code_verifier: codeVerifier,
          state
        })
      });

      const data = await response.json();

      console.log('Token response:', {
        status: response.status,
        hasToken: !!data.access_token,
        clientType: data.client_type
      });

      if (!response.ok || !data.access_token) {
        throw new Error(data.error || 'Failed to get token');
      }

      // Зберігаємо токен
      setToken(data.access_token);
      localStorage.setItem('oauth_access_token', data.access_token);
      
      // Очищаємо session storage
      sessionStorage.removeItem('oauth_code_verifier');
      sessionStorage.removeItem('oauth_state');

      console.log('✅ Authentication successful!');
      console.log('   Client type:', data.client_type);
      console.log('   Token saved to localStorage');
      console.log('=== Callback Complete ===\n');

      showSnackbar('Успішна авторизація!');

      // Перенаправлення на головну сторінку
      setTimeout(() => {
        window.location.href = '/';
      }, 1500);

    } catch (error) {
      console.error('❌ OAuth callback error:', error);
      showSnackbar('Помилка авторизації: ' + error.message, 'error');
      
      // Очищаємо дані
      sessionStorage.removeItem('oauth_code_verifier');
      sessionStorage.removeItem('oauth_state');
      
      setTimeout(() => {
        window.location.href = '/';
      }, 2000);
    } finally {
      setIsAuthenticating(false);
    }
  };

  const initiateGoogleLogin = async () => {
    console.log('\n=== Initiating React Web Client Authentication ===');
    
    try {
      // Генерація PKCE параметрів
      const codeVerifierBytes = generateRandomBytes(32);
      const codeVerifier = base64URLEncode(codeVerifierBytes);
      
      const codeChallenge = await generateCodeChallenge(codeVerifier);
      
      const stateBytes = generateRandomBytes(16);
      const state = base64URLEncode(stateBytes);

      console.log('Generated PKCE parameters:');
      console.log('  Code Verifier:', codeVerifier.substring(0, 10) + '...');
      console.log('  Code Challenge:', codeChallenge.substring(0, 10) + '...');
      console.log('  State:', state);

      // Зберігаємо PKCE дані в sessionStorage
      sessionStorage.setItem('oauth_code_verifier', codeVerifier);
      sessionStorage.setItem('oauth_state', state);

      // Створюємо структурований state об'єкт
      const stateObject = {
        value: state,
        client: CLIENT_TYPE,
        redirect: REDIRECT_URI
      };

      const stateJson = JSON.stringify(stateObject);
      console.log('State object:', stateJson);

      // Кодуємо state в Base64URL
      const stateBase64 = base64URLEncode(new TextEncoder().encode(stateJson));
      console.log('Encoded state:', stateBase64);

      // Формуємо URL авторизації
      const authUrl = `${API_URL}/auth/google?code_challenge=${encodeURIComponent(codeChallenge)}&state=${encodeURIComponent(stateBase64)}`;
      
      console.log('Redirecting to:', authUrl);
      console.log('Expected callback URL:', REDIRECT_URI);
      console.log('Client type:', CLIENT_TYPE);

      // Перенаправлення на сервер авторизації
      window.location.href = authUrl;
    } catch (error) {
      console.error('❌ Login initiation error:', error);
      showSnackbar('Помилка ініціації входу', 'error');
    }
  };

  const apiCall = async (url, options = {}) => {
    const headers = {
      'Authorization': `Bearer ${token}`,
      ...options.headers
    };

    const response = await fetch(`${API_URL}${url}`, {
      ...options,
      headers
    });

    if (response.status === 401) {
      logout();
      throw new Error('Unauthorized');
    }

    return response;
  };

  const fetchUserInfo = async () => {
    try {
      const response = await apiCall('/auth/me');
      const data = await response.json();
      setUser(data);
      console.log('User info:', data);
      console.log('Client type from token:', data.client_type);
    } catch (error) {
      console.error('Failed to fetch user info:', error);
    }
  };

  const fetchFolders = async () => {
    try {
      const url = currentFolder 
        ? `/api/folders?parent_id=${currentFolder.id}`
        : '/api/folders';
      const response = await apiCall(url);
      const data = await response.json();
      if (data.success) {
        setFolders(data.data);
      }
    } catch (error) {
      console.error('Failed to fetch folders:', error);
    }
  };

  const fetchAllFolders = async () => {
    try {
      const response = await apiCall('/api/folders/all');
      const data = await response.json();
      if (data.success) {
        setAllFolders(data.data);
      }
    } catch (error) {
      console.error('Failed to fetch all folders:', error);
    }
  };

  const fetchFiles = async () => {
    try {
      setLoading(true);
      let url = '/api/files';
      if (currentFolder) {
        url += `?folder_id=${currentFolder.id}`;
      }
      const response = await apiCall(url);
      const data = await response.json();
      if (data.success) {
        setFiles(data.data);
      }
    } catch (error) {
      console.error('Failed to fetch files:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleFileUpload = async (event) => {
    const file = event.target.files[0];
    if (!file) return;

    await uploadFile(file);
  };

  const uploadFile = async (file) => {
    const formData = new FormData();
    formData.append('file', file);
    if (currentFolder) {
      formData.append('folder_id', currentFolder.id);
    }

    try {
      setLoading(true);
      const response = await apiCall('/api/files/upload', {
        method: 'POST',
        body: formData,
        headers: {}
      });

      const data = await response.json();
      if (data.success) {
        fetchFiles();
        showSnackbar('Файл успішно завантажено!');
      }
    } catch (error) {
      console.error('Failed to upload file:', error);
      showSnackbar('Помилка завантаження файлу', 'error');
    } finally {
      setLoading(false);
    }
  };

  // Drag and Drop handlers
  const handleDragEnter = (e) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(true);
  };

  const handleDragLeave = (e) => {
    e.preventDefault();
    e.stopPropagation();
    
    // Перевіряємо чи дійсно виходимо за межі drop zone
    if (e.currentTarget === e.target) {
      setIsDragging(false);
    }
  };

  const handleDragOver = (e) => {
    e.preventDefault();
    e.stopPropagation();
  };

  const handleDrop = async (e) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);

    const files = Array.from(e.dataTransfer.files);
    
    if (files.length === 0) {
      showSnackbar('Файли не знайдено', 'warning');
      return;
    }

    if (files.length > 5) {
      showSnackbar('Можна завантажити максимум 5 файлів одночасно', 'warning');
      return;
    }

    showSnackbar(`Завантаження ${files.length} файл(ів)...`, 'info');

    for (const file of files) {
      await uploadFile(file);
    }
  };

  const handleFileDownload = async (fileId, fileName) => {
    try {
      const response = await apiCall(`/api/files/${fileId}/download`);
      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      a.click();
      window.URL.revokeObjectURL(url);
    } catch (error) {
      console.error('Failed to download file:', error);
      showSnackbar('Помилка завантаження файлу', 'error');
    }
  };

  const handleFileDelete = async (fileId) => {
    setConfirmDialog({
      open: true,
      title: 'Видалити файл',
      message: 'Ви впевнені, що хочете видалити цей файл?',
      onConfirm: async () => {
        try {
          const response = await apiCall(`/api/files/${fileId}`, {
            method: 'DELETE'
          });

          const data = await response.json();
          if (data.success) {
            fetchFiles();
            setSelectedFile(null);
            showSnackbar('Файл успішно видалено!');
          }
        } catch (error) {
          console.error('Failed to delete file:', error);
          showSnackbar('Помилка видалення файлу', 'error');
        }
      }
    });
  };

  const handleFilePreview = async (file) => {
    const ext = getFileExtension(file.name);
    const supportedTypes = ['cs', 'txt', 'js', 'json', 'xml', 'html', 'css'];

    if (!supportedTypes.includes(ext) && ext !== 'jpg' && ext !== 'png' && ext !== 'jpeg' && ext !== 'gif') {
      showSnackbar('Попередній перегляд недоступний для цього типу файлу', 'warning');
      return;
    }

    setSelectedFile(file);

    if (ext === 'jpg' || ext === 'png' || ext === 'jpeg' || ext === 'gif') {
      // Завантажуємо зображення як blob з авторизацією
      try {
        const response = await apiCall(`/api/files/${file.id}/download`);
        const blob = await response.blob();
        const imageUrl = window.URL.createObjectURL(blob);
        setFileContent(`IMAGE:${imageUrl}`);
      } catch (error) {
        console.error('Failed to load image:', error);
        setFileContent('Помилка завантаження зображення');
      }
      return;
    }

    try {
      const response = await apiCall(`/api/files/${file.id}/content`);
      const data = await response.json();
      if (data.success) {
        setFileContent(data.data);
      }
    } catch (error) {
      console.error('Failed to fetch file content:', error);
      setFileContent('Помилка завантаження вмісту файлу');
    }
  };

  const handleCreateFolder = async () => {
    if (!newFolderName.trim()) {
      showSnackbar('Введіть назву папки', 'warning');
      return;
    }

    try {
      const response = await apiCall('/api/folders', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: newFolderName,
          parent_id: currentFolder?.id || null
        })
      });

      const data = await response.json();
      if (data.success) {
        fetchFolders();
        fetchAllFolders();
        setShowNewFolderDialog(false);
        setNewFolderName('');
        showSnackbar('Папка успішно створена!');
      }
    } catch (error) {
      console.error('Failed to create folder:', error);
      showSnackbar('Помилка створення папки', 'error');
    }
  };

  const handleDeleteFolder = async (folderId) => {
    setConfirmDialog({
      open: true,
      title: 'Видалити папку',
      message: 'Ви впевнені? Це видалить папку та всі файли в ній.',
      onConfirm: async () => {
        try {
          const response = await apiCall(`/api/folders/${folderId}`, {
            method: 'DELETE'
          });

          const data = await response.json();
          if (data.success) {
            fetchFolders();
            fetchAllFolders();
            showSnackbar('Папку видалено!');
          }
        } catch (error) {
          console.error('Failed to delete folder:', error);
          showSnackbar('Помилка видалення папки', 'error');
        }
      }
    });
  };

  const logout = () => {
    setToken('');
    setUser(null);
    setFiles([]);
    setFolders([]);
    localStorage.removeItem('oauth_access_token');
    sessionStorage.removeItem('oauth_code_verifier');
    sessionStorage.removeItem('oauth_state');
    console.log('User logged out');
  };

  const getSortedAndFilteredFiles = () => {
    let filtered = [...files];

    if (filterType === 'js') {
      filtered = filtered.filter(f => getFileExtension(f.name) === 'js');
    } else if (filterType === 'png') {
      filtered = filtered.filter(f => getFileExtension(f.name) === 'png');
    }

    filtered.sort((a, b) => {
      let aVal, bVal;

      switch (sortBy) {
        case 'name':
          aVal = a.name.toLowerCase();
          bVal = b.name.toLowerCase();
          break;
        case 'type':
          aVal = getFileExtension(a.name);
          bVal = getFileExtension(b.name);
          break;
        default:
          return 0;
      }

      if (aVal < bVal) return sortOrder === 'asc' ? -1 : 1;
      if (aVal > bVal) return sortOrder === 'asc' ? 1 : -1;
      return 0;
    });

    return filtered;
  };

  const toggleSort = (field) => {
    if (sortBy === field) {
      setSortOrder(sortOrder === 'asc' ? 'desc' : 'asc');
    } else {
      setSortBy(field);
      setSortOrder('asc');
    }
  };

  // Якщо це callback сторінка і йде процес авторизації
  if (window.location.pathname === '/callback' || window.location.pathname === '/callback/') {
    return (
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <Box
          sx={{
            minHeight: '100vh',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            background: 'linear-gradient(135deg, #667eea 0%, #764ba2 100%)',
          }}
        >
          <Card sx={{ maxWidth: 500, width: '100%', boxShadow: 8, textAlign: 'center' }}>
            <CardContent sx={{ p: 5 }}>
              <CircularProgress size={64} sx={{ mb: 3 }} />
              <Typography variant="h5" gutterBottom fontWeight="medium">
                Обробка авторизації...
              </Typography>
              <Typography variant="body2" color="text.secondary">
                🌐 Web Client
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
                Зачекайте, відбувається перевірка та обмін токенів
              </Typography>
            </CardContent>
          </Card>
        </Box>
      </ThemeProvider>
    );
  }

  if (!token) {
    return (
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <Box
          sx={{
            minHeight: '100vh',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            background: 'linear-gradient(135deg, #667eea 0%, #764ba2 100%)',
            p: 2
          }}
        >
          <Card sx={{ maxWidth: 450, width: '100%', boxShadow: 8 }}>
            <CardContent sx={{ p: 5 }}>
              <Box sx={{ textAlign: 'center', mb: 4 }}>
                <Avatar
                  sx={{
                    width: 80,
                    height: 80,
                    bgcolor: 'primary.main',
                    mx: 'auto',
                    mb: 2
                  }}
                >
                  <FolderIcon sx={{ fontSize: 40 }} />
                </Avatar>
                <Typography variant="h4" fontWeight="bold" gutterBottom>
                  Cool Drive
                </Typography>
                <Typography variant="body1" color="text.secondary">
                  Увійдіть через Google для доступу до ваших файлів
                </Typography>
              </Box>

              {isAuthenticating ? (
                <Box sx={{ textAlign: 'center', py: 4 }}>
                  <CircularProgress size={48} sx={{ mb: 2 }} />
                  <Typography color="text.secondary">Авторизація...</Typography>
                </Box>
              ) : (
                <Button
                  variant="outlined"
                  fullWidth
                  size="large"
                  onClick={initiateGoogleLogin}
                  startIcon={
                    <svg width="20" height="20" viewBox="0 0 24 24">
                      <path fill="#4285F4" d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z"/>
                      <path fill="#34A853" d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z"/>
                      <path fill="#FBBC05" d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z"/>
                      <path fill="#EA4335" d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z"/>
                    </svg>
                  }
                  sx={{ py: 1.5 }}
                >
                  Увійти через Google
                </Button>
              )}
            </CardContent>
          </Card>
        </Box>
      </ThemeProvider>
    );
  }

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <Box sx={{ display: 'flex', minHeight: '100vh' }}>
        {/* Sidebar */}
        <Drawer
          variant="permanent"
          sx={{
            width: 280,
            flexShrink: 0,
            '& .MuiDrawer-paper': {
              width: 280,
              boxSizing: 'border-box',
              borderRight: '1px solid',
              borderColor: 'divider',
            },
          }}
        >
          <Box sx={{ p: 2, display: 'flex', alignItems: 'center', gap: 1 }}>
            <FolderIcon color="primary" sx={{ fontSize: 32 }} />
            <Typography variant="h6" fontWeight="bold">
              Cool Drive
            </Typography>
          </Box>
          <Divider />
          <List sx={{ p: 2 }}>
            <ListItem disablePadding sx={{ mb: 1 }}>
              <ListItemButton
                selected={!currentFolder}
                onClick={() => setCurrentFolder(null)}
                sx={{ borderRadius: 2 }}
              >
                <ListItemIcon>
                  <HomeIcon color={!currentFolder ? 'primary' : 'inherit'} />
                </ListItemIcon>
                <ListItemText primary="Всі файли" />
              </ListItemButton>
            </ListItem>

            <ListItem disablePadding sx={{ mb: 2 }}>
              <ListItemButton
                onClick={() => setShowNewFolderDialog(true)}
                sx={{ borderRadius: 2 }}
              >
                <ListItemIcon>
                  <AddIcon />
                </ListItemIcon>
                <ListItemText primary="Нова папка" />
              </ListItemButton>
            </ListItem>

            <Divider sx={{ my: 2 }} />

            <Typography variant="caption" color="text.secondary" sx={{ px: 2, mb: 1, display: 'block' }}>
              ПАПКИ
            </Typography>

            {folders.map(folder => (
              <ListItem
                key={folder.id}
                disablePadding
                sx={{ mb: 0.5 }}
                secondaryAction={
                  <IconButton
                    size="small"
                    onClick={(e) => {
                      e.stopPropagation();
                      setSelectedFolder(folder);
                      setAnchorEl(e.currentTarget);
                    }}
                  >
                    <MoreVertIcon fontSize="small" />
                  </IconButton>
                }
              >
                <ListItemButton
                  onClick={() => setCurrentFolder(folder)}
                  sx={{ borderRadius: 2 }}
                >
                  <ListItemIcon>
                    <FolderIcon />
                  </ListItemIcon>
                  <ListItemText 
                    primary={folder.name}
                    primaryTypographyProps={{ variant: 'body2' }}
                  />
                </ListItemButton>
              </ListItem>
            ))}
          </List>
        </Drawer>

        {/* Main Content */}
        <Box component="main" sx={{ flexGrow: 1, display: 'flex', flexDirection: 'column' }}>
          {/* AppBar */}
          <AppBar position="static" color="default" elevation={0} sx={{ borderBottom: 1, borderColor: 'divider' }}>
            <Toolbar>
              <Breadcrumbs sx={{ flexGrow: 1 }}>
                <Link
                  component="button"
                  variant="body1"
                  onClick={() => setCurrentFolder(null)}
                  underline="hover"
                  color="inherit"
                  sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}
                >
                  <HomeIcon fontSize="small" />
                  Головна
                </Link>
                {breadcrumbPath.map((folder, index) => (
                  <Link
                    key={folder.id}
                    component="button"
                    variant="body1"
                    onClick={() => setCurrentFolder(folder)}
                    underline="hover"
                    color={index === breadcrumbPath.length - 1 ? 'text.primary' : 'inherit'}
                    sx={{ fontWeight: index === breadcrumbPath.length - 1 ? 'medium' : 'normal' }}
                  >
                    {folder.name}
                  </Link>
                ))}
              </Breadcrumbs>

              <Chip
                avatar={<Avatar>{user?.name?.[0] || user?.email?.[0]}</Avatar>}
                label={user?.name || user?.email}
                sx={{ mr: 2 }}
              />

              <Tooltip title="Вийти">
                <IconButton onClick={logout} color="inherit">
                  <LogoutIcon />
                </IconButton>
              </Tooltip>
            </Toolbar>
          </AppBar>

          {/* Toolbar */}
          <Paper sx={{ m: 3, p: 2, mb: 2 }} elevation={0}>
            <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap">
              <Button
                component="label"
                variant="contained"
                startIcon={<UploadIcon />}
                disableElevation
              >
                Завантажити файл
                <input
                  type="file"
                  hidden
                  onChange={handleFileUpload}
                  multiple
                />
              </Button>

              <Button
                variant="outlined"
                startIcon={<SettingsIcon />}
                onClick={() => setShowColumnSettings(!showColumnSettings)}
              >
                Стовпці
              </Button>

              <Box sx={{ flexGrow: 1 }} />

              <FormControl size="small" sx={{ minWidth: 150 }}>
                <InputLabel>Фільтр</InputLabel>
                <Select
                  value={filterType}
                  label="Фільтр"
                  onChange={(e) => setFilterType(e.target.value)}
                  startAdornment={<FilterListIcon fontSize="small" sx={{ mr: 1 }} />}
                >
                  <MenuItem value="all">Всі файли</MenuItem>
                  <MenuItem value="js">Тільки .js</MenuItem>
                  <MenuItem value="png">Тільки .png</MenuItem>
                </Select>
              </FormControl>
            </Stack>

            {showColumnSettings && (
              <Paper variant="outlined" sx={{ mt: 2, p: 2 }}>
                <Typography variant="subtitle2" gutterBottom fontWeight="medium">
                  Видимі стовпці:
                </Typography>
                <FormGroup row>
                  {Object.entries(visibleColumns).map(([key, value]) => (
                    key !== 'name' && (
                      <FormControlLabel
                        key={key}
                        control={
                          <Checkbox
                            checked={value}
                            onChange={(e) => setVisibleColumns({...visibleColumns, [key]: e.target.checked})}
                            size="small"
                          />
                        }
                        label={key}
                      />
                    )
                  ))}
                </FormGroup>
              </Paper>
            )}
          </Paper>

          {/* Files Table */}
          <Box 
            sx={{ mx: 3, mb: 3, flexGrow: 1, position: 'relative' }}
            onDragEnter={handleDragEnter}
            onDragOver={handleDragOver}
            onDragLeave={handleDragLeave}
            onDrop={handleDrop}
          >
            {/* Drag and Drop Overlay */}
            {isDragging && (
              <Box
                sx={{
                  position: 'absolute',
                  top: 0,
                  left: 0,
                  right: 0,
                  bottom: 0,
                  bgcolor: 'rgba(25, 118, 210, 0.1)',
                  border: '3px dashed',
                  borderColor: 'primary.main',
                  borderRadius: 2,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  zIndex: 1000,
                  pointerEvents: 'none'
                }}
              >
                <Box sx={{ textAlign: 'center' }}>
                  <CloudUploadIcon sx={{ fontSize: 80, color: 'primary.main', mb: 2 }} />
                  <Typography variant="h5" fontWeight="bold" color="primary">
                    Відпустіть файли для завантаження
                  </Typography>
                  <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                    Підтримується до 5 файлів одночасно
                  </Typography>
                </Box>
              </Box>
            )}

            <TableContainer component={Paper} elevation={0}>
              <Table>
                <TableHead>
                  <TableRow>
                    <TableCell>
                        Назва
                    </TableCell>
                    {visibleColumns.type && (
                      <TableCell>
                        <Button
                          size="small"
                          onClick={() => toggleSort('type')}
                          endIcon={<SortIcon fontSize="small" />}
                        >
                          Тип
                        </Button>
                      </TableCell>
                    )}
                    {visibleColumns.size && (
                      <TableCell>
                          Розмір
                      </TableCell>
                    )}
                    {visibleColumns.created && (
                      <TableCell>
                          Створено
                      </TableCell>
                    )}
                    {visibleColumns.updated && (
                      <TableCell>Оновлено</TableCell>
                    )}
                    {visibleColumns.uploadedBy && (
                      <TableCell>Завантажив</TableCell>
                    )}
                    {visibleColumns.editedBy && (
                      <TableCell>Редагував</TableCell>
                    )}
                    <TableCell align="right">Дії</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {loading ? (
                    <TableRow>
                      <TableCell colSpan={8} align="center" sx={{ py: 8 }}>
                        <CircularProgress />
                        <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
                          Завантаження...
                        </Typography>
                      </TableCell>
                    </TableRow>
                  ) : getSortedAndFilteredFiles().length === 0 ? (
                    <TableRow>
                      <TableCell colSpan={8} align="center" sx={{ py: 8 }}>
                        <CloudUploadIcon sx={{ fontSize: 64, color: 'text.disabled', mb: 2 }} />
                        <Typography variant="h6" color="text.secondary" gutterBottom>
                          Файлів не знайдено
                        </Typography>
                        <Typography variant="body2" color="text.secondary">
                          Перетягніть файли сюди або натисніть "Завантажити файл"
                        </Typography>
                      </TableCell>
                    </TableRow>
                  ) : (
                    getSortedAndFilteredFiles().map(file => (
                      <TableRow key={file.id} hover>
                        <TableCell>
                          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                            <FileIcon color="action" />
                            <Typography variant="body2" fontWeight="medium">
                              {file.name}
                            </Typography>
                          </Box>
                        </TableCell>
                        {visibleColumns.type && (
                          <TableCell>
                            <Chip
                              label={`.${getFileExtension(file.name)}`}
                              size="small"
                              variant="outlined"
                            />
                          </TableCell>
                        )}
                        {visibleColumns.size && (
                          <TableCell>
                            <Typography variant="body2" color="text.secondary">
                              {formatFileSize(file.size)}
                            </Typography>
                          </TableCell>
                        )}
                        {visibleColumns.created && (
                          <TableCell>
                            <Typography variant="body2" color="text.secondary">
                              {formatDate(file.created_at)}
                            </Typography>
                          </TableCell>
                        )}
                        {visibleColumns.updated && (
                          <TableCell>
                            <Typography variant="body2" color="text.secondary">
                              {formatDate(file.updated_at)}
                            </Typography>
                          </TableCell>
                        )}
                        {visibleColumns.uploadedBy && (
                          <TableCell>
                            <Typography variant="body2" color="text.secondary">
                              {file.uploaded_by_name || 'N/A'}
                            </Typography>
                          </TableCell>
                        )}
                        {visibleColumns.editedBy && (
                          <TableCell>
                            <Typography variant="body2" color="text.secondary">
                              {file.edited_by_name || 'N/A'}
                            </Typography>
                          </TableCell>
                        )}
                        <TableCell align="right">
                          <Stack direction="row" spacing={0.5} justifyContent="flex-end">
                            <Tooltip title="Переглянути">
                              <IconButton
                                size="small"
                                color="primary"
                                onClick={() => handleFilePreview(file)}
                              >
                                <VisibilityIcon fontSize="small" />
                              </IconButton>
                            </Tooltip>
                            <Tooltip title="Завантажити">
                              <IconButton
                                size="small"
                                color="success"
                                onClick={() => handleFileDownload(file.id, file.name)}
                              >
                                <DownloadIcon fontSize="small" />
                              </IconButton>
                            </Tooltip>
                            <Tooltip title="Видалити">
                              <IconButton
                                size="small"
                                color="error"
                                onClick={() => handleFileDelete(file.id)}
                              >
                                <DeleteIcon fontSize="small" />
                              </IconButton>
                            </Tooltip>
                          </Stack>
                        </TableCell>
                      </TableRow>
                    ))
                  )}
                </TableBody>
              </Table>
            </TableContainer>
          </Box>
        </Box>
      </Box>

      {/* Folder Menu */}
      <Menu
        anchorEl={anchorEl}
        open={Boolean(anchorEl)}
        onClose={() => {
          setAnchorEl(null);
          setSelectedFolder(null);
        }}
      >
        <MenuItem
          onClick={() => {
            handleDeleteFolder(selectedFolder.id);
            setAnchorEl(null);
            setSelectedFolder(null);
          }}
        >
          <ListItemIcon>
            <DeleteIcon fontSize="small" color="error" />
          </ListItemIcon>
          <ListItemText>Видалити папку</ListItemText>
        </MenuItem>
      </Menu>

      {/* File Preview Dialog */}
      <Dialog
        open={Boolean(selectedFile)}
        onClose={() => {
          if (fileContent.startsWith('IMAGE:')) {
            window.URL.revokeObjectURL(fileContent.substring(6));
          }
          setSelectedFile(null);
          setFileContent('');
        }}
        maxWidth="md"
        fullWidth
      >
        <DialogTitle>
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <Typography variant="h6">{selectedFile?.name}</Typography>
            <IconButton
              onClick={() => {
                if (fileContent.startsWith('IMAGE:')) {
                  window.URL.revokeObjectURL(fileContent.substring(6));
                }
                setSelectedFile(null);
                setFileContent('');
              }}
            >
              <CloseIcon />
            </IconButton>
          </Box>
        </DialogTitle>
        <DialogContent dividers>
          {fileContent.startsWith('IMAGE:') ? (
            <Box sx={{ textAlign: 'center' }}>
              <img
                src={fileContent.substring(6)}
                alt={selectedFile?.name}
                style={{ maxWidth: '100%', height: 'auto' }}
              />
            </Box>
          ) : (
            <Paper variant="outlined" sx={{ p: 2, bgcolor: 'grey.50' }}>
              <pre style={{ margin: 0, overflow: 'auto', fontSize: '0.875rem' }}>
                <code>{fileContent}</code>
              </pre>
            </Paper>
          )}
        </DialogContent>
      </Dialog>

      {/* New Folder Dialog */}
      <Dialog
        open={showNewFolderDialog}
        onClose={() => {
          setShowNewFolderDialog(false);
          setNewFolderName('');
        }}
        maxWidth="sm"
        fullWidth
      >
        <DialogTitle>Створити нову папку</DialogTitle>
        <DialogContent>
          <TextField
            autoFocus
            fullWidth
            label="Назва папки"
            value={newFolderName}
            onChange={(e) => setNewFolderName(e.target.value)}
            onKeyPress={(e) => e.key === 'Enter' && handleCreateFolder()}
            sx={{ mt: 2 }}
          />
        </DialogContent>
        <DialogActions>
          <Button
            onClick={() => {
              setShowNewFolderDialog(false);
              setNewFolderName('');
            }}
          >
            Скасувати
          </Button>
          <Button onClick={handleCreateFolder} variant="contained">
            Створити
          </Button>
        </DialogActions>
      </Dialog>

      {/* Confirm Dialog */}
      <Dialog
        open={confirmDialog.open}
        onClose={() => setConfirmDialog({ open: false, title: '', message: '', onConfirm: null })}
        maxWidth="xs"
        fullWidth
      >
        <DialogTitle>{confirmDialog.title}</DialogTitle>
        <DialogContent>
          <Typography>{confirmDialog.message}</Typography>
        </DialogContent>
        <DialogActions>
          <Button
            onClick={() => setConfirmDialog({ open: false, title: '', message: '', onConfirm: null })}
          >
            Скасувати
          </Button>
          <Button
            onClick={() => {
              confirmDialog.onConfirm();
              setConfirmDialog({ open: false, title: '', message: '', onConfirm: null });
            }}
            variant="contained"
            color="error"
          >
            Видалити
          </Button>
        </DialogActions>
      </Dialog>

      {/* Snackbar */}
      <Snackbar
        open={snackbar.open}
        autoHideDuration={4000}
        onClose={() => setSnackbar({ ...snackbar, open: false })}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
      >
        <Alert
          onClose={() => setSnackbar({ ...snackbar, open: false })}
          severity={snackbar.severity}
          variant="filled"
          sx={{ width: '100%' }}
        >
          {snackbar.message}
        </Alert>
      </Snackbar>
    </ThemeProvider>
  );
}