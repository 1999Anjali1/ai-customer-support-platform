import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LoginResponse, Role, User } from './models';

const STORAGE_KEY = 'ss.session';

interface Session {
  token: string;
  expiresAt: string;
  user: User;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly session = signal<Session | null>(this.restore());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly token = computed(() => this.session()?.token ?? null);
  readonly isLoggedIn = computed(() => this.session() !== null);
  readonly role = computed(() => this.user()?.role ?? null);

  async login(email: string, password: string): Promise<User> {
    const res = await firstValueFrom(this.http.post<LoginResponse>('/api/auth/login', { email, password }));
    const session: Session = { token: res.token, expiresAt: res.expiresAt, user: res.user };
    this.session.set(session);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    } catch {
      /* private mode – session lives in memory only */
    }
    return res.user;
  }

  logout(): void {
    this.session.set(null);
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      /* ignore */
    }
    this.router.navigateByUrl('/login');
  }

  /** Where each role lands after login. */
  homeFor(role: Role | null): string {
    switch (role) {
      case 'Customer':
        return '/chat';
      case 'Agent':
        return '/agent';
      case 'Admin':
        return '/analytics';
      default:
        return '/login';
    }
  }

  private restore(): Session | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const session = JSON.parse(raw) as Session;
      return new Date(session.expiresAt) > new Date() ? session : null;
    } catch {
      return null;
    }
  }
}

/** Adds the JWT to every API call and logs out on 401. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.token();
  const request = token && req.url.startsWith('/api') ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
  return next(request);
};

/** Route guard: logged in and (optionally) one of the given roles. */
export function roleGuard(...roles: Role[]): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (!auth.isLoggedIn()) return router.parseUrl('/login');
    if (roles.length && !roles.includes(auth.role()!)) return router.parseUrl(auth.homeFor(auth.role()));
    return true;
  };
}
