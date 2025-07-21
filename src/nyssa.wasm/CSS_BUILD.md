# CSS Build Process

This project uses Tailwind CSS for styling. The CSS is built from source files and needs to be compiled before running the application.

## Prerequisites
- Node.js and npm installed
- Dependencies installed: `npm install`

## Building CSS

### Development (with watch mode)
```bash
npm run watch-css
```

### Production (minified)
```bash
npm run build-css
```

## CSS Architecture

- **Source**: `Styles/app.css` - Contains Tailwind directives and custom component styles
- **Output**: `wwwroot/css/app.css` - Compiled CSS file used by the application

## Component Styles

Custom component styles are defined in `app.css` using Tailwind's @apply directive for consistency:
- `.card` - Card components with shadow and hover effects
- `.btn` - Button styles with primary and secondary variants
- `.nav-link` - Navigation link styles
- `.form-input` - Form input styling
- `.container-custom` - Responsive container with padding
- `.section` - Section spacing utilities

## Responsive Design

The app follows a mobile-first approach with breakpoints:
- Default: Mobile styles
- `sm:` - 640px and up
- `md:` - 768px and up
- `lg:` - 1024px and up
- `xl:` - 1280px and up