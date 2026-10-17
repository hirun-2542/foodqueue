import type { Metadata } from "next";
import { CustomerMenuScreen } from "./menu-screen";

export const metadata: Metadata = {
  title: "สั่งอาหาร · FoodQueue",
};

/**
 * The token is the only thing the server has to read here, and it is a path segment: nothing about
 * this route can be prerendered, because every token is a different round. `instant = false` is the
 * framework's own escape hatch for that (Next 16 cacheComponents otherwise refuses the route as a
 * blocking one) — the screen paints from its own skeleton while the API answers.
 */
export const instant = false;

/**
 * `/s/{token}` — the QR a guest scans at the table. The token is the identity: no auth, no account,
 * one round of one table. The screen itself is a client component because the session info, the menu
 * and the clock offset all come from the API (tickets 09, 10 and 13 share this tree).
 */
export default async function CustomerPage({ params }: PageProps<"/s/[token]">) {
  const { token } = await params;
  return <CustomerMenuScreen token={token} />;
}
