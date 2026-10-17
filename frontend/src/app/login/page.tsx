import type { Metadata } from "next";
import { LoginForm } from "./login-form";

export const metadata: Metadata = {
  title: "เข้าสู่ระบบ · FoodQueue",
};

export default function LoginPage() {
  return <LoginForm />;
}
