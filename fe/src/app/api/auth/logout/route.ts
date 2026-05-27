import { NextResponse } from 'next/server';

const COOKIE_NAME = 'uav_token';

/**
 * POST /api/auth/logout
 *
 * Clears the uav_token HttpOnly cookie, effectively logging the user out.
 * No upstream call is needed — JWT is stateless. The token will expire
 * naturally on the backend; we simply destroy it on the client side.
 */
export async function POST(): Promise<NextResponse> {
  const response = NextResponse.json(
    { message: 'Logged out successfully.' },
    { status: 200 }
  );

  // Expire the cookie immediately
  response.cookies.set(COOKIE_NAME, '', {
    httpOnly: true,
    secure: process.env.NODE_ENV === 'production',
    sameSite: 'strict',
    path: '/',
    maxAge: 0,
  });

  return response;
}
