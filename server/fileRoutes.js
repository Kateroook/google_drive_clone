const express = require('express');
const multer = require('multer');
const path = require('path');
const fs = require('fs').promises;
const fsSync = require('fs');
const { authenticateJWT } = require('./authRoutes');

const router = express.Router();

// Налаштування multer для завантаження файлів
const storage = multer.diskStorage({
  destination: async (req, file, cb) => {
    const uploadDir = path.join(__dirname, 'uploads');
    
    // Створення папки якщо не існує
    if (!fsSync.existsSync(uploadDir)) {
      await fs.mkdir(uploadDir, { recursive: true });
    }
    
    cb(null, uploadDir);
  },
  filename: (req, file, cb) => {
    // Генерація унікального імені файлу
    const uniqueSuffix = Date.now() + '-' + Math.round(Math.random() * 1E9);
    cb(null, uniqueSuffix + '-' + file.originalname);
  }
});

const upload = multer({ 
  storage: storage,
  limits: { fileSize: 100 * 1024 * 1024 } // 100MB limit
});


// 1. Отримання списку файлів
router.get('/api/files', authenticateJWT, async (req, res) => {
  try {
    const userId = req.user.id;
    const folderId = req.query.folder_id;

    let query = `
      SELECT f.id, f.name, f.folder_id, f.path, f.created_at, f.updated_at,
             uploader.name as uploaded_by_name, uploader.id as uploaded_by,
             editor.name as edited_by_name, editor.id as edited_by
      FROM files f
      LEFT JOIN users uploader ON f.uploaded_by = uploader.id
      LEFT JOIN users editor ON f.edited_by = editor.id
      WHERE f.uploaded_by = ?
    `;
    
    const params = [userId];

    // Якщо folder_id передано явно (навіть якщо 'null' або '0')
    if (folderId !== undefined) {
      if (folderId === 'null' || folderId === '0' || folderId === '') {
        // Тільки файли БЕЗ папки
        query += ' AND f.folder_id IS NULL';
      } else {
        // Файли конкретної папки
        query += ' AND f.folder_id = ?';
        params.push(folderId);
      }
    }
    // Якщо folder_id не передано взагалі - повертаємо всі файли (для "All Files")

    query += ' ORDER BY f.created_at DESC';

    const [files] = await db.query(query, params);

    // Додаємо розмір файлу
    for (const file of files) {
      try {
        const stats = await fs.stat(file.path);
        file.size = stats.size;
      } catch (err) {
        file.size = 0;
      }
    }

    res.json({
      success: true,
      data: files
    });
  } catch (error) {
    console.error('Error fetching files:', error);
    res.status(500).json({ 
      success: false, 
      error: 'Failed to fetch files' 
    });
  }
});

// 2. Завантаження файлу на сервер
router.post('/api/files/upload', authenticateJWT, upload.single('file'), async (req, res) => {
  try {
    const userId = req.user.id;
    const folderId = req.body.folder_id || null;
    const file = req.file;

    if (!file) {
      return res.status(400).json({ 
        success: false, 
        error: 'No file uploaded' 
      });
    }

    console.log(`File uploaded: ${file.originalname} by user ${userId}`);

    // Збереження інформації про файл в БД
    const [result] = await db.query(
      `INSERT INTO files (name, folder_id, uploaded_by, path, created_at, updated_at)
       VALUES (?, ?, ?, ?, NOW(), NOW())`,
      [file.originalname, folderId, userId, file.path]
    );

    const [newFile] = await db.query(
      `SELECT f.id, f.name, f.folder_id, f.path, f.created_at, f.updated_at,
              u.name as uploaded_by_name, u.id as uploaded_by
       FROM files f
       LEFT JOIN users u ON f.uploaded_by = u.id
       WHERE f.id = ?`,
      [result.insertId]
    );

    // Додаємо розмір файлу
    const stats = await fs.stat(file.path);
    newFile[0].size = stats.size;

    res.json({
      success: true,
      data: newFile[0]
    });
  } catch (error) {
    console.error('Error uploading file:', error);
    res.status(500).json({ 
      success: false, 
      error: 'Failed to upload file' 
    });
  }
});

// 3. Завантаження файлу з сервера
router.get('/api/files/:id/download', authenticateJWT, async (req, res) => {
  try {
    const userId = req.user.id;
    const fileId = req.params.id;

    const [files] = await db.query(
      'SELECT * FROM files WHERE id = ? AND uploaded_by = ?',
      [fileId, userId]
    );

    if (files.length === 0) {
      return res.status(404).json({ 
        success: false, 
        error: 'File not found' 
      });
    }

    const file = files[0];

    // Перевірка чи файл існує
    if (!fsSync.existsSync(file.path)) {
      return res.status(404).json({ 
        success: false, 
        error: 'File not found on disk' 
      });
    }

    console.log(`File downloaded: ${file.name} by user ${userId}`);

    res.download(file.path, file.name);
  } catch (error) {
    console.error('Error downloading file:', error);
    res.status(500).json({ 
      success: false, 
      error: 'Failed to download file' 
    });
  }
});

// 4. Отримання вмісту файлу для попереднього перегляду
router.get('/api/files/:id/content', authenticateJWT, async (req, res) => {
  try {
    const userId = req.user.id;
    const fileId = req.params.id;

    const [files] = await db.query(
      'SELECT * FROM files WHERE id = ? AND uploaded_by = ?',
      [fileId, userId]
    );

    if (files.length === 0) {
      return res.status(404).json({ 
        success: false, 
        error: 'File not found' 
      });
    }

    const file = files[0];
    const ext = path.extname(file.name).toLowerCase();

    // Підтримка тільки текстових файлів
    const textExtensions = ['.txt', '.cs', '.py', '.js', '.json', '.xml', '.html', '.css', '.cpp', '.java'];
    
    if (!textExtensions.includes(ext)) {
      return res.status(400).json({ 
        success: false, 
        error: 'File type not supported for preview' 
      });
    }

    const content = await fs.readFile(file.path, 'utf8');
    
    // Обмеження розміру для попереднього перегляду (1MB)
    if (content.length > 1024 * 1024) {
      return res.json({
        success: true,
        data: content.substring(0, 1024 * 1024) + '\n\n... (content truncated)'
      });
    }

    res.json({
      success: true,
      data: content
    });
  } catch (error) {
    console.error('Error getting file content:', error);
    res.status(500).json({ 
      success: false, 
      error: 'Failed to get file content' 
    });
  }
});

// 5. Видалення файлу
router.delete('/api/files/:id', authenticateJWT, async (req, res) => {
  try {
    const userId = req.user.id;
    const fileId = req.params.id;

    const [files] = await db.query(
      'SELECT * FROM files WHERE id = ? AND uploaded_by = ?',
      [fileId, userId]
    );

    if (files.length === 0) {
      return res.status(404).json({ 
        success: false, 
        error: 'File not found' 
      });
    }

    const file = files[0];

    // Видалення файлу з диску
    try {
      await fs.unlink(file.path);
    } catch (err) {
      console.warn(`Could not delete file from disk: ${file.path}`, err);
    }

    // Видалення запису з БД
    await db.query('DELETE FROM files WHERE id = ?', [fileId]);

    console.log(`File deleted: ${file.name} by user ${userId}`);

    res.json({
      success: true,
      message: 'File deleted successfully'
    });
  } catch (error) {
    console.error('Error deleting file:', error);
    res.status(500).json({ 
      success: false, 
      error: 'Failed to delete file' 
    });
  }
});

// 6. Отримання папок
router.get('/api/folders', authenticateJWT, async (req, res) => {
  try {
    const userId = req.user.id;
    const parentId = req.query.parent_id;

    let query = `
      SELECT id, name, parent_id, created_at, updated_at, sync_path
      FROM folders
      WHERE user_id = ?
    `;
    
    const params = [userId];

    if (parentId) {
      query += ' AND parent_id = ?';
      params.push(parentId);
    } else {
      query += ' AND parent_id IS NULL';
    }

    query += ' ORDER BY name ASC';

    const [folders] = await db.query(query, params);

    res.json({
      success: true,
      data: folders
    });
  } catch (error) {
    console.error('Error fetching folders:', error);
    res.status(500).json({ 
      success: false, 
      error: 'Failed to fetch folders' 
    });
  }
});


// Отримання ВСІХ папок користувача (незалежно від parent_id)
router.get('/api/folders/all', authenticateJWT, async (req, res) => {
  try {
    const userId = req.user.id;
    
    const query = `
      SELECT id, name, parent_id, sync_path, created_at, updated_at
      FROM folders
      WHERE user_id = ?
      ORDER BY name ASC
    `;
    
    const [folders] = await db.query(query, [userId]);
    
    res.json({
      success: true,
      data: folders
    });
  } catch (error) {
    console.error('Error fetching all folders:', error);
    res.status(500).json({ 
      success: false, 
      error: 'Failed to fetch folders' 
    });
  }
});

// 7. Створення папки
router.post('/api/folders', authenticateJWT, async (req, res) => {
  try {
    const userId = req.user.id;
    const { name, parent_id, sync_path } = req.body;

    if (!name) {
      return res.status(400).json({ 
        success: false, 
        error: 'Folder name is required' 
      });
    }

    if(!sync_path) { console.warn('No sync_path provided for folder creation'); }

    const [result] = await db.query(
      `INSERT INTO folders (name, parent_id, user_id, created_at, updated_at, sync_path)
       VALUES (?, ?, ?, NOW(), NOW(), ?)`,
      [name, parent_id || null, userId, sync_path || null]
    );

    console.log(`Folder created: ${name} by user ${userId}`);

    res.status(201).json({
      success: true,
      data: {
        id: result.insertId,
        name,
        parent_id: parent_id || null,
        user_id: userId,
        created_at: new Date(),
        updated_at: new Date(),
        sync_path: sync_path || null
      }
    });
  } catch (error) {
    console.error('Error creating folder:', error);
    res.status(500).json({ 
      success: false, 
      error: 'Failed to create folder' 
    });
  }
});

// 8. Отримання інформації про файл
router.get('/api/files/:id', authenticateJWT, async (req, res) => {
  try {
    const userId = req.user.id;
    const fileId = req.params.id;

    const [files] = await db.query(
      `SELECT f.*, 
              uploader.name as uploaded_by_name,
              editor.name as edited_by_name
       FROM files f
       LEFT JOIN users uploader ON f.uploaded_by = uploader.id
       LEFT JOIN users editor ON f.edited_by = editor.id
       WHERE f.id = ? AND f.uploaded_by = ?`,
      [fileId, userId]
    );

    if (files.length === 0) {
      return res.status(404).json({ 
        success: false, 
        error: 'File not found' 
      });
    }

    const file = files[0];

    // Додаємо розмір файлу
    try {
      const stats = await fs.stat(file.path);
      file.size = stats.size;
    } catch (err) {
      file.size = 0;
    }

    res.json({
      success: true,
      data: file
    });
  } catch (error) {
    console.error('Error fetching file:', error);
    res.status(500).json({ 
      success: false, 
      error: 'Failed to fetch file' 
    });
  }
});

// 9. Оновлення вмісту файлу (заміна), збереження created_at, оновлення updated_at
router.put('/api/files/:id', authenticateJWT, upload.single('file'), async (req, res) => {
  try {
    const userId = req.user.id;
    const fileId = req.params.id;
    const newFile = req.file;

    if (!newFile) {
      return res.status(400).json({ success: false, error: 'No file provided' });
    }

    const [files] = await db.query(
      'SELECT * FROM files WHERE id = ? AND uploaded_by = ?',
      [fileId, userId]
    );

    if (files.length === 0) {
      return res.status(404).json({ success: false, error: 'File not found' });
    }

    const existing = files[0];

    // Перезаписуємо вміст у той самий шлях; якщо шлях не існує, перенесемо з тимчасового
    try {
      if (fsSync.existsSync(existing.path)) {
        const buffer = await fs.readFile(newFile.path);
        await fs.writeFile(existing.path, buffer);
        // видаляємо тимчасовий завантажений файл
        try { await fs.unlink(newFile.path); } catch (_) {}
      } else {
        // якщо оригінальний шлях зник — переносимо новий файл на його місце
        await fs.mkdir(path.dirname(existing.path), { recursive: true });
        await fs.rename(newFile.path, existing.path);
      }
    } catch (ioErr) {
      console.error('Error replacing file content:', ioErr);
      return res.status(500).json({ success: false, error: 'Failed to update file content' });
    }

    // Оновлюємо метадані (ім'я може залишатися старим; збережемо оригінальне)
    await db.query(
      'UPDATE files SET updated_at = NOW(), edited_by = ? WHERE id = ?',
      [userId, fileId]
    );

    const [updatedRows] = await db.query(
      `SELECT f.id, f.name, f.folder_id, f.path, f.created_at, f.updated_at,
              uploader.name as uploaded_by_name, uploader.id as uploaded_by,
              editor.name as edited_by_name, editor.id as edited_by
       FROM files f
       LEFT JOIN users uploader ON f.uploaded_by = uploader.id
       LEFT JOIN users editor ON f.edited_by = editor.id
       WHERE f.id = ?`,
      [fileId]
    );

    const updated = updatedRows[0];
    try {
      const stats = await fs.stat(updated.path);
      updated.size = stats.size;
    } catch (_) {
      updated.size = 0;
    }

    console.log(`File updated: ${existing.name} by user ${userId}`);
    res.json({ success: true, data: updated });
  } catch (error) {
    console.error('Error updating file:', error);
    res.status(500).json({ success: false, error: 'Failed to update file' });
  }
});


// 10. Оновлення папки (встановлення/видалення синхронізації)
router.put('/api/folders/:id', authenticateJWT, async (req, res) => {
  try {
    const userId = req.user.id;
    const folderId = req.params.id;
    const { sync_path } = req.body;

    const [folders] = await db.query(
      'SELECT * FROM folders WHERE id = ? AND user_id = ?',
      [folderId, userId]
    );

    if (folders.length === 0) {
      return res.status(404).json({ success: false, error: 'Folder not found' });
    }

    await db.query(
      'UPDATE folders SET sync_path = ?, updated_at = NOW() WHERE id = ?',
      [sync_path || null, folderId]
    );

    const [updated] = await db.query(
      'SELECT * FROM folders WHERE id = ?',
      [folderId]
    );

    console.log(`Folder sync updated: ${updated[0].name}, path: ${sync_path || 'removed'}`);

    res.json({
      success: true,
      data: updated[0]
    });
  } catch (error) {
    console.error('Error updating folder:', error);
    res.status(500).json({ success: false, error: 'Failed to update folder' });
  }
});

// 11. Видалення папки (каскадне видалення файлів)
router.delete('/api/folders/:id', authenticateJWT, async (req, res) => {
  try {
    const userId = req.user.id;
    const folderId = req.params.id;

    const [folders] = await db.query(
      'SELECT * FROM folders WHERE id = ? AND user_id = ?',
      [folderId, userId]
    );

    if (folders.length === 0) {
      return res.status(404).json({ success: false, error: 'Folder not found' });
    }

    const folder = folders[0];

    // Видалити всі файли в папці
    const [files] = await db.query(
      'SELECT * FROM files WHERE folder_id = ?',
      [folderId]
    );

    for (const file of files) {
      try {
        await fs.unlink(file.path);
      } catch (err) {
        console.warn(`Could not delete file from disk: ${file.path}`);
      }
    }

    await db.query('DELETE FROM files WHERE folder_id = ?', [folderId]);

    // Видалити підпапки рекурсивно
    const [subfolders] = await db.query(
      'SELECT id FROM folders WHERE parent_id = ?',
      [folderId]
    );

    for (const subfolder of subfolders) {
      // Рекурсивно видаляємо через той самий ендпоінт
      await deleteFolder(subfolder.id, userId);
    }

    // Видалити саму папку
    await db.query('DELETE FROM folders WHERE id = ?', [folderId]);

    console.log(`Folder deleted: ${folder.name} by user ${userId}`);

    res.json({
      success: true,
      message: 'Folder deleted successfully'
    });
  } catch (error) {
    console.error('Error deleting folder:', error);
    res.status(500).json({ success: false, error: 'Failed to delete folder' });
  }
});

// Допоміжна функція для рекурсивного видалення
async function deleteFolder(folderId, userId) {
  const [files] = await db.query(
    'SELECT * FROM files WHERE folder_id = ?',
    [folderId]
  );

  for (const file of files) {
    try {
      await fs.unlink(file.path);
    } catch (err) {}
  }

  await db.query('DELETE FROM files WHERE folder_id = ?', [folderId]);

  const [subfolders] = await db.query(
    'SELECT id FROM folders WHERE parent_id = ?',
    [folderId]
  );

  for (const subfolder of subfolders) {
    await deleteFolder(subfolder.id, userId);
  }

  await db.query('DELETE FROM folders WHERE id = ?', [folderId]);
}



module.exports = router;