import type { Metadata, Viewport } from "next";
import { DM_Mono, IBM_Plex_Sans_Thai } from "next/font/google";
import { Providers } from "./providers";
import "./globals.css";

const plexThai = IBM_Plex_Sans_Thai({
  variable: "--font-plex-thai",
  weight: ["400", "500", "600"],
  subsets: ["thai", "latin"],
  display: "swap",
});

// Table codes, clock figures and receipt numbers only; never Thai text (DESIGN: Split Run Rule).
const dmMono = DM_Mono({
  variable: "--font-dm-mono",
  weight: "500",
  subsets: ["latin"],
  display: "swap",
});

export const metadata: Metadata = {
  title: "FoodQueue",
  description: "ระบบสั่งอาหารผ่าน QR สำหรับร้านชาบูและบุฟเฟ่ต์",
};

export const viewport: Viewport = {
  themeColor: "#fafaf9",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="th" className={`${plexThai.variable} ${dmMono.variable} h-full`}>
      <body className="flex min-h-full flex-col">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
