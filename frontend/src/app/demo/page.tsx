import type { Metadata } from "next";
import { DemoScreen } from "./demo-screen";

export const metadata: Metadata = {
  title: "เดโม 3 มุมมอง · FoodQueue",
};

/**
 * `/demo` — frame `d1-demo`, the split screen of ticket 26 (spec story 108): the customer, kitchen and
 * owner views side by side with the bot panel.
 *
 * The page is a client screen because everything on it is: the demo switch (`GET /api/demo/status`), the
 * demo sign-in, and the three live documents it frames. Nothing about it can be prerendered — with the
 * demo off there is no page to draw at all.
 */
export default function DemoPage() {
  return <DemoScreen />;
}
