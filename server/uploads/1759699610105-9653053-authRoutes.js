const express = require('express');
const passport = require('passport');
const GoogleStrategy = require('passport-google-oauth20').Strategy;
const jwt = require('jsonwebtoken');
const crypto = require('crypto');
const router = express.Router();

// Конфігурація
const JWT_SECRET = process.env.JWT_SECRET || 'your-secret-key';
const JWT_EXPIRES_IN = '7d';
const GOOGLE_CLIENT_ID = process.env.GOOGLE_OAUTH_CLIENT_ID;
const GOOGLE_CLIENT_SECRET = process.env.GOOGLE_OAUTH_CLIENT_SECRET;
const SERVER_URL = process.env.SERVER_URL;

// Сховище для PKCE кодів
const pkceStore = new Map();

// Функція очищення старих записів
const cleanupExpiredEntries = () => {
  const now = Date.now();
  for (const [key, value] of pkceStore.entries()) {
    if (now - value.timestamp > 600000) { // 10 хвилин
      pkceStore.delete(key);
      console.log(`Cleaned up expired entry: ${key}`);
    }
  }
};

// Очищення кожні 5 хвилин
setInterval(cleanupExpiredEntries, 300000);

// Налаштування Google Strategy
passport.use(new GoogleStrategy({
    clientID: GOOGLE_CLIENT_ID,
    clientSecret: GOOGLE_CLIENT_SECRET,
    callbackURL: `${SERVER_URL}/auth/google/callback`,
    scope: ['profile', 'email']
  },
  async (accessToken, refreshToken, profile, done) => {
    try {
      console.log('Google authentication successful');
      console.log(`User: ${profile.displayName} (${profile.emails[0].value})`);
      
      const { id, emails, displayName } = profile;
      const email = emails[0].value;

      // Перевірка чи користувач існує
      const [existingUser] = await db.query(
        'SELECT * FROM users WHERE google_id = ?',
        [id]
      );

      if (existingUser.length > 0) {
        console.log(`Existing user found: ${existingUser[0].id}`);
        return done(null, existingUser[0]);
      }

      // Створення нового користувача
      console.log('Creating new user...');
      const [result] = await db.query(
        'INSERT INTO users (google_id, email, name) VALUES (?, ?, ?)',
        [id, email, displayName]
      );

      const newUser = {
        id: result.insertId,
        google_id: id,
        email,
        name: displayName
      };

      console.log(`New user created with ID: ${newUser.id}`);
      return done(null, newUser);
    } catch (error) {
      console.error('Error in Google Strategy:', error);
      return done(error, null);
    }
  }
));

// Middleware для перевірки JWT
const authenticateJWT = (req, res, next) => {
  const authHeader = req.headers.authorization;

  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    return res.status(401).json({ error: 'Unauthorized' });
  }

  const token = authHeader.substring(7);

  try {
    const decoded = jwt.verify(token, JWT_SECRET);
    req.user = decoded;
    next();
  } catch (error) {
    console.error('JWT verification failed:', error.message);
    return res.status(403).json({ error: 'Invalid or expired token' });
  }
};

// 1. Ініціація OAuth процесу з PKCE
router.get('/auth/google', (req, res, next) => {
  const { code_challenge, state } = req.query;

  console.log('=== OAuth Initialization ===');
  console.log(`Code Challenge: ${code_challenge}`);
  console.log(`State: ${state}`);

  if (!code_challenge || !state) {
    console.error('Missing code_challenge or state');
    return res.status(400).json({ 
      error: 'code_challenge and state are required' 
    });
  }

  // Зберігаємо PKCE параметри
  pkceStore.set(state, {
    code_challenge,
    timestamp: Date.now()
  });

  console.log(`PKCE data stored for state: ${state}`);
  console.log(`Current pkceStore size: ${pkceStore.size}`);

  // Перенаправлення на Google OAuth з state
  passport.authenticate('google', {
    scope: ['profile', 'email'],
    state: state,
    session: false
  })(req, res, next);
});

// 2. Callback від Google
router.get('/auth/google/callback',
  passport.authenticate('google', { 
    session: false,
    failureRedirect: '/auth/error'
  }),
  (req, res) => {
    console.log('=== Google Callback Received ===');
    
    const state = req.query.state;
    const user = req.user;

    console.log(`State from callback: ${state}`);
    console.log(`User: ${user.email} (ID: ${user.id})`);

    if (!state) {
      console.error('No state parameter in callback');
      return res.redirect('/auth/error');
    }

    // Генерація тимчасового authorization code
    const authCode = crypto.randomBytes(32).toString('hex');
    console.log(`Generated auth code: ${authCode.substring(0, 10)}...`);
    
    // Зберігаємо код з користувачем та state
    pkceStore.set(authCode, {
      user,
      state,
      timestamp: Date.now()
    });

    console.log(`Auth code stored. pkceStore size: ${pkceStore.size}`);

    // Перенаправлення назад до WPF застосунку
    const redirectUrl = `http://localhost:5005/callback?code=${authCode}&state=${state}`;
    console.log(`Redirecting to: ${redirectUrl}`);
    res.redirect(redirectUrl);
  }
);

// 3. Обмін authorization code на JWT токен
router.post('/auth/token', async (req, res) => {
  console.log('=== Token Exchange Request ===');
  console.log('Request body:', JSON.stringify(req.body, null, 2));
  
  const { code, code_verifier, state } = req.body;

  if (!code || !code_verifier || !state) {
    console.error('Missing required parameters');
    return res.status(400).json({ 
      error: 'code, code_verifier, and state are required' 
    });
  }

  console.log(`Code: ${code.substring(0, 10)}...`);
  console.log(`Code Verifier: ${code_verifier.substring(0, 10)}...`);
  console.log(`State: ${state}`);
  console.log(`pkceStore size: ${pkceStore.size}`);
  console.log(`pkceStore keys: ${Array.from(pkceStore.keys()).join(', ')}`);

  // Отримання даних з authorization code
  const authData = pkceStore.get(code);
  if (!authData) {
    console.error(`Auth code not found in store: ${code.substring(0, 10)}...`);
    console.log('Available codes:', Array.from(pkceStore.keys()).map(k => k.substring(0, 10)).join(', '));
    return res.status(400).json({ error: 'Invalid or expired code' });
  }

  console.log(`Auth data found. User: ${authData.user.email}`);

  // Перевірка state
  if (authData.state !== state) {
    console.error(`State mismatch! Expected: ${authData.state}, Got: ${state}`);
    pkceStore.delete(code);
    return res.status(400).json({ error: 'State mismatch' });
  }

  console.log('State verified successfully');

  // Отримання збереженого code_challenge
  const pkceData = pkceStore.get(state);
  if (!pkceData) {
    console.error(`PKCE data not found for state: ${state}`);
    console.log('Available states:', Array.from(pkceStore.keys()).filter(k => k.length < 50).join(', '));
    pkceStore.delete(code);
    return res.status(400).json({ error: 'PKCE data not found' });
  }

  console.log(`PKCE data found. Code challenge: ${pkceData.code_challenge.substring(0, 10)}...`);

  // Верифікація PKCE
  const hash = crypto
    .createHash('sha256')
    .update(code_verifier)
    .digest('base64')
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=/g, '');

  console.log(`Computed hash: ${hash.substring(0, 10)}...`);
  console.log(`Expected hash: ${pkceData.code_challenge.substring(0, 10)}...`);

  if (hash !== pkceData.code_challenge) {
    console.error('PKCE verification failed!');
    pkceStore.delete(code);
    pkceStore.delete(state);
    return res.status(400).json({ error: 'Invalid code_verifier' });
  }

  console.log('PKCE verification successful');

  // Видалення використаних кодів
  pkceStore.delete(code);
  pkceStore.delete(state);
  console.log(`Cleaned up. pkceStore size: ${pkceStore.size}`);

  // Генерація JWT токену
  const token = jwt.sign(
    {
      id: authData.user.id,
      email: authData.user.email,
      name: authData.user.name
    },
    JWT_SECRET,
    { expiresIn: JWT_EXPIRES_IN }
  );

  console.log(`JWT token generated for user: ${authData.user.email}`);
  console.log('=== Token Exchange Successful ===\n');

  res.json({
    access_token: token,
    token_type: 'Bearer',
    expires_in: 604800 // 7 днів в секундах
  });
});

// 4. Отримання інформації про поточного користувача
router.get('/auth/me', authenticateJWT, (req, res) => {
  console.log(`User info requested for: ${req.user.email}`);
  res.json({
    id: req.user.id,
    email: req.user.email,
    name: req.user.name
  });
});

// 5. Refresh токену
router.post('/auth/refresh', authenticateJWT, (req, res) => {
  console.log(`Token refresh requested for user: ${req.user.email}`);
  
  const newToken = jwt.sign(
    {
      id: req.user.id,
      email: req.user.email,
      name: req.user.name
    },
    JWT_SECRET,
    { expiresIn: JWT_EXPIRES_IN }
  );

  res.json({
    access_token: newToken,
    token_type: 'Bearer',
    expires_in: 604800
  });
});

// Обробка помилок
router.get('/auth/error', (req, res) => {
  console.error('Authentication error endpoint reached');
  res.redirect('http://localhost:5005/callback?error=authentication_failed');
});

// Тестовий ендпоінт для перевірки
router.get('/auth/status', (req, res) => {
  res.json({
    status: 'ok',
    pkceStoreSize: pkceStore.size,
    serverTime: new Date().toISOString()
  });
});

module.exports = { router, authenticateJWT };
