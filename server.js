const express = require('express');
const cors = require('cors');
const passport = require('passport');
const mysql = require('mysql2/promise');
require('dotenv').config();
const fs = require('fs');
const { router: authRouter, authenticateJWT } = require('./authRoutes');
const fileRouter = require('./fileRoutes');
const app = express();
const PORT = process.env.PORT || 5000;

// Підключення до SingleStore
const pool = mysql.createPool({
  host: process.env.SINGLESTORE_HOST,
  user: process.env.SINGLESTORE_USER,
  password: process.env.SINGLESTORE_PASSWORD,
  database: process.env.SINGLESTORE_DATABASE,
  port: process.env.SINGLESTORE_PORT,
  ssl: {
    ca: fs.readFileSync("./certs/singlestore_bundle.pem")
  },
  connectionLimit: 10,
});

// Зробити pool доступним глобально
global.db = pool;

// Middleware
app.use(cors({
  origin: '*', 
  credentials: true
}));

app.use(express.json());
app.use(express.urlencoded({ extended: true }));
app.use(passport.initialize());

// Маршрути 
app.use(authRouter);
app.use(fileRouter);

// Логування всіх запитів
app.use((req, res, next) => {
  console.log(`${new Date().toISOString()} - ${req.method} ${req.path}`);
  next();
});


// Health check
app.get('/health', (req, res) => {
  res.json({ status: 'OK', timestamp: new Date().toISOString() });
});

// 404 handler
app.use((req, res) => {
  res.status(404).json({ 
    success: false, 
    error: 'Route not found' 
  });
});

// Error handler
app.use((err, req, res, next) => {
  console.error('Server error:', err);
  res.status(500).json({ 
    success: false, 
    error: 'Internal server error' 
  });
});

// Запуск сервера
app.listen(PORT, () => {
  console.log(`Server is running on http://localhost:${PORT}`);
});

module.exports = app;