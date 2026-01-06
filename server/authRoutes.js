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
const SERVER_URL = process.env.SERVER_URL || 'http://localhost:5000';

// Конфігурація клієнтів з їх redirect URIs
const CLIENT_CONFIGS = {
  desktop: {
    name: 'WPF Desktop',
    redirectUri: 'http://localhost:5005/callback',
    allowedOrigins: ['http://localhost:5005']
  },
  web: {
    name: 'React Web',
    redirectUri: 'http://localhost:3000/callback',
    allowedOrigins: ['http://localhost:3000']
  }
};

// Валідація конфігурації
if (!GOOGLE_CLIENT_ID || !GOOGLE_CLIENT_SECRET) {
  console.error('❌ ERROR: GOOGLE_OAUTH_CLIENT_ID and GOOGLE_OAUTH_CLIENT_SECRET must be set in .env');
  process.exit(1);
}

console.log('✅ Google OAuth Configuration:');
console.log('   Client ID:', GOOGLE_CLIENT_ID.substring(0, 20) + '...');
console.log('   Callback URL:', `${SERVER_URL}/auth/google/callback`);
console.log('   Configured clients:', Object.keys(CLIENT_CONFIGS).join(', '));

// Сховище для PKCE кодів та session даних
const pkceStore = new Map();

// Функція очищення старих записів
const cleanupExpiredEntries = () => {
  const now = Date.now();
  for (const [key, value] of pkceStore.entries()) {
    if (now - value.timestamp > 600000) { // 10 хвилин
      pkceStore.delete(key);
      console.log(`🧹 Cleaned up expired entry: ${key}`);
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
    scope: ['profile', 'email'],
    passReqToCallback: true
  },
  async (req, accessToken, refreshToken, profile, done) => {
    try {
      console.log('✅ Google authentication successful');
      console.log(`   User: ${profile.displayName} (${profile.emails[0].value})`);

      const { id, emails, displayName } = profile;
      const email = emails[0].value;

      // Перевірка чи користувач існує
      const [existingUser] = await db.query(
        'SELECT * FROM users WHERE google_id = ?',
        [id]
      );

      if (existingUser.length > 0) {
        console.log(`   Existing user found: ${existingUser[0].id}`);
        return done(null, existingUser[0]);
      }

      // Створення нового користувача
      console.log('   Creating new user...');
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

      console.log(`   New user created with ID: ${newUser.id}`);
      return done(null, newUser);
    } catch (error) {
      console.error('❌ Error in Google Strategy:', error);
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

// Функція декодування state (з Base64URL)
const decodeState = (stateParam) => {
  try {
    // Відновлюємо Base64 з Base64URL
    let base64 = stateParam
      .replace(/-/g, '+')
      .replace(/_/g, '/');
    
    // Додаємо padding якщо потрібно
    while (base64.length % 4) {
      base64 += '=';
    }
    
    const jsonString = Buffer.from(base64, 'base64').toString('utf8');
    return JSON.parse(jsonString);
  } catch (error) {
    console.error('❌ Failed to decode state:', error.message);
    return null;
  }
};

// Функція отримання redirect URI на основі client type
const getRedirectUri = (clientType) => {
  const config = CLIENT_CONFIGS[clientType];
  if (!config) {
    console.warn(`⚠️  Unknown client type: ${clientType}, using default`);
    return CLIENT_CONFIGS.web.redirectUri;
  }
  return config.redirectUri;
};

// 1. Ініціація OAuth процесу з PKCE
router.get('/auth/google', (req, res, next) => {
  const { code_challenge, state: stateParam } = req.query;

  console.log('\n=== OAuth Initialization ===');
  console.log('Code Challenge:', code_challenge?.substring(0, 10) + '...');
  console.log('State (encoded):', stateParam?.substring(0, 30) + '...');

  if (!code_challenge || !stateParam) {
    console.error('❌ Missing code_challenge or state');
    return res.status(400).json({ 
      error: 'code_challenge and state are required' 
    });
  }

  // Декодуємо state для отримання інформації про клієнта
  const stateData = decodeState(stateParam);
  
  if (!stateData || !stateData.value || !stateData.client) {
    console.error('❌ Invalid state format');
    return res.status(400).json({ 
      error: 'Invalid state format. Expected: {value, client, redirect}' 
    });
  }

  const { value: stateValue, client: clientType, redirect: clientRedirect } = stateData;

  console.log('State Value:', stateValue);
  console.log('Client Type:', clientType);
  console.log('Client Redirect:', clientRedirect);

  // Визначаємо redirect URI
  const redirectUri = clientRedirect || getRedirectUri(clientType);
  
  // Валідація client type
  if (!CLIENT_CONFIGS[clientType]) {
    console.error('❌ Unknown client type:', clientType);
    return res.status(400).json({ 
      error: 'Invalid client type',
      allowed_clients: Object.keys(CLIENT_CONFIGS)
    });
  }

  console.log('✅ Client identified:', CLIENT_CONFIGS[clientType].name);
  console.log('   Redirect URI:', redirectUri);

  // Зберігаємо PKCE параметри + інформацію про клієнта
  pkceStore.set(stateValue, {
    code_challenge,
    clientType,
    redirectUri,
    timestamp: Date.now()
  });

  console.log('✅ PKCE data stored for state:', stateValue);
  console.log('   Current pkceStore size:', pkceStore.size);

  // Перенаправлення на Google OAuth з оригінальним state
  passport.authenticate('google', {
    scope: ['profile', 'email'],
    state: stateParam, // Передаємо оригінальний закодований state
    session: false,
    accessType: 'offline',
    prompt: 'consent'
  })(req, res, next);
});

// 2. Callback від Google
router.get('/auth/google/callback',
  (req, res, next) => {
    console.log('\n=== Google Callback Received ===');
    console.log('Query params:', req.query);
    
    passport.authenticate('google', { 
      session: false,
      failureRedirect: '/auth/error'
    })(req, res, next);
  },
  (req, res) => {
    const stateParam = req.query.state;
    const user = req.user;

    if (!stateParam) {
      console.error('❌ No state parameter in callback');
      return res.redirect('/auth/error?error=no_state');
    }

    if (!user) {
      console.error('❌ No user in callback');
      return res.redirect('/auth/error?error=no_user');
    }

    // Декодуємо state
    const stateData = decodeState(stateParam);
    
    if (!stateData || !stateData.value) {
      console.error('❌ Invalid state in callback');
      return res.redirect('/auth/error?error=invalid_state');
    }

    const stateValue = stateData.value;

    console.log('✅ Callback successful');
    console.log('   State Value:', stateValue);
    console.log('   User:', user.email, '(ID:', user.id + ')');

    // Отримуємо збережені дані клієнта
    const pkceData = pkceStore.get(stateValue);
    
    if (!pkceData) {
      console.error('❌ PKCE data not found for state:', stateValue);
      return res.redirect('/auth/error?error=session_expired');
    }

    const { clientType, redirectUri } = pkceData;
    const clientConfig = CLIENT_CONFIGS[clientType];

    console.log('   Client Type:', clientType, `(${clientConfig.name})`);
    console.log('   Redirect URI:', redirectUri);

    // Генерація тимчасового authorization code
    const authCode = crypto.randomBytes(32).toString('hex');
    console.log('   Generated auth code:', authCode.substring(0, 10) + '...');

    // Зберігаємо код з користувачем та state
    pkceStore.set(authCode, {
      user,
      stateValue,
      clientType,
      timestamp: Date.now()
    });

    console.log('   Auth code stored. pkceStore size:', pkceStore.size);

    // Перенаправлення до відповідного клієнта
    const redirectUrl = `${redirectUri}${redirectUri.includes('?') ? '&' : '?'}code=${authCode}&state=${stateValue}`;
    console.log(`   Redirecting to ${clientConfig.name}:`, redirectUrl);
    console.log('=== Callback Complete ===\n');
    
    res.redirect(redirectUrl);
  }
);

// 3. Обмін authorization code на JWT токен
router.post('/auth/token', async (req, res) => {
  console.log('\n=== Token Exchange Request ===');
  console.log('Request body:', JSON.stringify(req.body, null, 2));

  const { code, code_verifier, state } = req.body;

  if (!code || !code_verifier || !state) {
    console.error('❌ Missing required parameters');
    return res.status(400).json({ 
      error: 'code, code_verifier, and state are required' 
    });
  }

  console.log('Code:', code.substring(0, 10) + '...');
  console.log('Code Verifier:', code_verifier.substring(0, 10) + '...');
  console.log('State:', state);
  console.log('pkceStore size:', pkceStore.size);

  // Отримання даних з authorization code
  const authData = pkceStore.get(code);
  if (!authData) {
    console.error('❌ Auth code not found in store');
    return res.status(400).json({ error: 'Invalid or expired code' });
  }

  const { user, stateValue, clientType } = authData;
  const clientConfig = CLIENT_CONFIGS[clientType];

  console.log('✅ Auth data found');
  console.log('   User:', user.email);
  console.log('   Client:', clientConfig.name);

  // Перевірка state
  if (stateValue !== state) {
    console.error(`❌ State mismatch! Expected: ${stateValue}, Got: ${state}`);
    pkceStore.delete(code);
    return res.status(400).json({ error: 'State mismatch' });
  }

  console.log('✅ State verified successfully');

  // Отримання збереженого code_challenge
  const pkceData = pkceStore.get(state);
  if (!pkceData) {
    console.error('❌ PKCE data not found for state:', state);
    pkceStore.delete(code);
    return res.status(400).json({ error: 'PKCE data not found' });
  }

  console.log('✅ PKCE data found');

  // Верифікація PKCE
  const hash = crypto
    .createHash('sha256')
    .update(code_verifier)
    .digest('base64')
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=/g, '');

  if (hash !== pkceData.code_challenge) {
    console.error('❌ PKCE verification failed!');
    pkceStore.delete(code);
    pkceStore.delete(state);
    return res.status(400).json({ error: 'Invalid code_verifier' });
  }

  console.log('✅ PKCE verification successful');

  // Видалення використаних кодів
  pkceStore.delete(code);
  pkceStore.delete(state);
  console.log('✅ Cleaned up. pkceStore size:', pkceStore.size);

  // Генерація JWT токену з інформацією про клієнта
  const token = jwt.sign(
    {
      id: user.id,
      email: user.email,
      name: user.name,
      client: clientType // Додаємо інформацію про клієнта в токен
    },
    JWT_SECRET,
    { expiresIn: JWT_EXPIRES_IN }
  );

  console.log(`✅ JWT token generated for ${clientConfig.name} user:`, user.email);
  console.log('=== Token Exchange Successful ===\n');

  res.json({
    access_token: token,
    token_type: 'Bearer',
    expires_in: 604800, // 7 днів в секундах
    client_type: clientType
  });
});

// 4. Отримання інформації про поточного користувача
router.get('/auth/me', authenticateJWT, (req, res) => {
  console.log(`ℹ️  User info requested for: ${req.user.email} (${req.user.client || 'unknown'} client)`);
  res.json({
    id: req.user.id,
    email: req.user.email,
    name: req.user.name,
    client_type: req.user.client
  });
});

// 5. Refresh токену
router.post('/auth/refresh', authenticateJWT, (req, res) => {
  console.log(`ℹ️  Token refresh requested for user: ${req.user.email} (${req.user.client || 'unknown'} client)`);

  const newToken = jwt.sign(
    {
      id: req.user.id,
      email: req.user.email,
      name: req.user.name,
      client: req.user.client
    },
    JWT_SECRET,
    { expiresIn: JWT_EXPIRES_IN }
  );

  res.json({
    access_token: newToken,
    token_type: 'Bearer',
    expires_in: 604800,
    client_type: req.user.client
  });
});

// Обробка помилок з автоматичним визначенням клієнта
router.get('/auth/error', (req, res) => {
  const error = req.query.error || 'authentication_failed';
  const stateParam = req.query.state;
  
  console.error('❌ Authentication error endpoint reached:', error);
  
  let redirectUri = CLIENT_CONFIGS.web.redirectUri; // Default
  
  // Спробуємо визначити клієнта зі state
  if (stateParam) {
    const stateData = decodeState(stateParam);
    if (stateData?.value) {
      const pkceData = pkceStore.get(stateData.value);
      if (pkceData?.redirectUri) {
        redirectUri = pkceData.redirectUri;
        console.log('   Using stored redirect URI:', redirectUri);
      }
    }
  }
  
  console.log('   Redirecting to:', redirectUri);
  res.redirect(`${redirectUri}?error=${error}`);
});

// Тестовий ендпоінт для перевірки
router.get('/auth/status', (req, res) => {
  res.json({
    status: 'ok',
    pkceStoreSize: pkceStore.size,
    serverTime: new Date().toISOString(),
    config: {
      clientIdSet: !!GOOGLE_CLIENT_ID,
      clientSecretSet: !!GOOGLE_CLIENT_SECRET,
      callbackUrl: `${SERVER_URL}/auth/google/callback`,
      clients: Object.entries(CLIENT_CONFIGS).map(([key, config]) => ({
        type: key,
        name: config.name,
        redirectUri: config.redirectUri
      }))
    }
  });
});

module.exports = { router, authenticateJWT };