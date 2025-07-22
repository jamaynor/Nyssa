/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./**/*.{razor,html,cshtml}",
    "./wwwroot/index.html"
  ],
  theme: {
    extend: {
      colors: {
        primary: '#2563eb',
        secondary: '#1e40af',
      }
    },
  },
  plugins: [],
}