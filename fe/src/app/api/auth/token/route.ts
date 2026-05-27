import { NextRequest, NextResponse } from 'next/server';
import { jwtVerify } from 'jose';

/**
 * GET /api/auth/token
 *
 * Returns the raw JWT token string from the HttpOnly cookie.
 * This is required ONLY for the @microsoft/signalr client, which needs
 * to pass the token as a query parameter (?access_token=...) for WebSocket
 * authentication — the standard SignalR auth pattern for SPAs.
 *
 * Security note: The token is held only in JavaScript memory (never
 * persisted to localStorage/sessionStorage). The WS connection to Kong's
 * /ws/alerts hub is still fully JWT-validated server-side.
 */
export async function GET(request: NextRequest): Promise<NextResponse> {
  const token = request.cookies.get('uav_token')?.value;

  if (!token) {
    return NextResponse.json({ message: 'Not authenticated.' }, { status: 401 });
  }

  const secret = process.env.JWT_SECRET;
  if (!secret) {
    return NextResponse.json({ message: 'Server configuration error.' }, { status: 500 });
  }

  // Verify the token is still valid before handing it out
  try {
    await jwtVerify(token, new TextEncoder().encode(secret), {
      issuer: process.env.JWT_ISSUER ?? 'uav-detection-system',
      audience: process.env.JWT_AUDIENCE ?? 'uav-supervisors',
      algorithms: ['HS256'],
    });
  } catch {
    return NextResponse.json({ message: 'Invalid or expired session.' }, { status: 401 });
  }

  return NextResponse.json({ token });
}
