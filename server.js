require("dotenv").config();
const express = require("express");
const mysql = require("mysql2/promise");
const { OAuth2Client } = require("google-auth-library");
const fs = require("fs");

const app = express();
app.use(express.json());

// Пул підключень
let pool;
(async () => {
  pool = await mysql.createPool({
    host: process.env.SINGLESTORE_HOST,
    port: 3333,
    user: process.env.SINGLESTORE_USER,
    password: process.env.SINGLESTORE_PASSWORD,
    database: process.env.SINGLESTORE_DATABASE,
    ssl: {
      ca: fs.readFileSync("./certs/singlestore_bundle.pem"),
    },
    connectionLimit: 10,
  });
    console.log("Pool created, running test query...");
    const [rows] = await pool.query('SELECT CURRENT_USER() as user, DATABASE() as db, 1 as ok');
    console.log('DB test:', rows);
    console.log("✅ Connected to SingleStore with SSL");
})();

// Google OAuth
const oauth2Client = new OAuth2Client(
  process.env.GOOGLE_CLIENT_ID,
  process.env.GOOGLE_CLIENT_SECRET,
  process.env.GOOGLE_REDIRECT_URI
);

let latestToken = null;

// Функція для збереження користувача в базі
async function saveUser({ conn, googleId, email, name }) {
    const [result] = await conn.execute(
        `INSERT INTO users (google_id, email, name) 
         VALUES (?, ?, ?)
         ON DUPLICATE KEY UPDATE name = VALUES(name), google_id = VALUES(google_id)`,
        [googleId, email, name]
    );
    return result.insertId;
}

// Маршрут для входу через Google
app.get("/auth/google", (req, res) => {
    const url = oauth2Client.generateAuthUrl({
        access_type: "offline",
        scope: ["profile", "email"],
    });
    res.redirect(url);
});

// Callback після авторизації Google
app.get("/auth/google/callback", async (req, res) => {
    try {
        const { code } = req.query;
        const { tokens } = await oauth2Client.getToken(code);
        oauth2Client.setCredentials(tokens);

        const ticket = await oauth2Client.verifyIdToken({
            idToken: tokens.id_token,
            audience: process.env.GOOGLE_CLIENT_ID,
        });

        const payload = ticket.getPayload();
        const googleId = payload.sub;
        const email = payload.email;
        const name = payload.name;

        // Збереження користувача через пул
        const conn = await pool.getConnection();
        try {
            const userId = await saveUser({ conn, googleId, email, name });
            console.log("User saved with ID:", userId);
        } finally {
            conn.release();
        }

        // Зберігаємо токен для клієнта
        latestToken = tokens.id_token;

        res.send("✅ Google login successful. You may return to the app.");
    } catch (err) {
        console.error("OAuth callback error:", err);
        res.status(500).send("OAuth login failed");
    }
});

// Endpoint для Desktop App, щоб забрати токен
app.get("/auth/google/token", (req, res) => {
    if (latestToken) {
        res.send(latestToken);
        latestToken = null; // видаляємо після видачі
    } else {
        res.status(404).send("No token yet");
    }
});

// Запуск сервера
app.listen(5000, () => {
    console.log("Server running on http://localhost:5000");
});
