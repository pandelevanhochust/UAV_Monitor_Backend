import { NextRequest, NextResponse } from 'next/server';
import { jwtVerify } from 'jose';

/**
 * GET /api/auth/me
 *
 * Server-side endpoint that reads the HttpOnly uav_token cookie,
 * verifies the JWT, and returns the decoded user identity claims.
 *
 * Used by the useAuth() SWR hook to populate the current user context
 * without ever exposing the raw token to browser JavaScript.
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

  try {
    const { payload } = await jwtVerify(
      token,
      new TextEncoder().encode(secret),
      {
        issuer: process.env.JWT_ISSUER ?? 'uav-detection-system',
        audience: process.env.JWT_AUDIENCE ?? 'uav-supervisors',
        algorithms: ['HS256'],
      }
    );

    return NextResponse.json({
      id: payload.sub,
      role: payload['role'],
      email: payload['email'] ?? null,
      name: payload['name'] ?? null,
    });
  } catch {
    return NextResponse.json({ message: 'Invalid or expired session.' }, { status: 401 });
  }
}
