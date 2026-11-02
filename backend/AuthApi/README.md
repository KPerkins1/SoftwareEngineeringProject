# CTF Portal API

## Local setup

Install the .NET 10 SDK. In VS Code, run the **Start CTF Portal** task. The API terminal prompts for the bootstrap admin email and password; it generates a temporary signing key and starts the API. Keep the password so you can sign in. The first run creates the admin account; later runs must use the same email and password.

You can also start the API manually from the project directory with `powershell -ExecutionPolicy Bypass -File backend/AuthApi/start-dev.ps1`, then run `npm start` in another terminal.

Keep the bootstrap password out of source control. User and CTF records are stored in JSON files under the operating system's local application data directory by default. Set `CTF_DATA_DIRECTORY` to choose another persistent directory.

The combined task also starts Angular. To start it separately from the project directory:

```powershell
npm start
```

The Angular development proxy forwards `/api` requests to `http://localhost:5000`. Open `http://localhost:4200` and sign in as the bootstrap admin. Admins can create instructor accounts; instructors and admins can create CTFs or import JSON.

## Import format

A file may contain one CTF object, an array of CTF objects, or an object with a `ctfs` array. Each CTF needs `title`, `description`, `category`, `startsAt`, and `endsAt`. Dates should be ISO 8601 strings with a timezone offset.

```json
{
  "title": "Spring Security Jam",
  "description": "A team-based web security competition.",
  "category": "Jeopardy",
  "startsAt": "2027-04-10T09:00:00Z",
  "endsAt": "2027-04-10T17:00:00Z"
}
```

Imports are limited to 50 events and 1 MB. This first version imports event metadata, not executable challenge packages.

## Production notes

Serve the API over HTTPS and configure `CTF_WEB_ORIGIN` to the exact trusted frontend origin. The production session cookie is Secure, HttpOnly, and SameSite=Strict; state-changing requests also require a CSRF token. Passwords are stored as PBKDF2 hashes. The JSON file store is suitable for local development and a single-process demo; replace it with a transactional database before multi-instance or production deployment.
