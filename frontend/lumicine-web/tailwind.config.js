/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        background: {
          DEFAULT: "#F9F9F6", // Nền trắng ngà
          subtle: "#F3F3EE",
          surface: "#FFFFFF",
        },
        foreground: {
          DEFAULT: "#18181B", // Chữ tối
          muted: "#71717A",
          subtle: "#A1A1AA",
        },
        primary: {
          DEFAULT: "#166534", // Xanh lá đậm cho hành động chính
          hover: "#14532D",
          light: "#DCFCE7",
          foreground: "#FFFFFF",
        },
        accent: {
          DEFAULT: "#0F766E",
          hover: "#115E59",
        },
        border: {
          DEFAULT: "#E4E4E7",
          strong: "#D4D4D8",
        },
        status: {
          available: "#166534",
          held: "#D97706",
          sold: "#DC2626",
          selected: "#2563EB",
          cancelled: "#6B7280",
        }
      },
      fontFamily: {
        sans: ['"DM Sans"', 'system-ui', 'sans-serif'],
        heading: ['"Barlow Condensed"', 'sans-serif'],
      },
    },
  },
  plugins: [],
}
