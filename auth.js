const fetch = require("node-fetch");

async function authMiddleware(req, res, next) {
  const authHeader = req.headers["authorization"];
  if (!authHeader) return res.status(401).json({ error: "No token" });

  const token = authHeader.split(" ")[1];

  try {
    const response = await fetch("https://www.googleapis.com/oauth2/v3/userinfo", {
      headers: { Authorization: `Bearer ${token}` }
    });
    const userInfo = await response.json();

    if (userInfo.error) {
      return res.status(401).json({ error: "Invalid token" });
    }

    req.user = userInfo; // { sub, name, email, picture }
    next();
  } catch (err) {
    res.status(500).json({ error: "Auth error" });
  }
}

module.exports = authMiddleware;
