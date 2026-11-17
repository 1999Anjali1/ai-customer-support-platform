import { Routes } from '@angular/router';
import { roleGuard } from './core/auth.service';
import { ShellComponent } from './layout/shell.component';

// Every page is lazy-loaded, so customers never download the agent/admin code.
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./pages/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [roleGuard()],
    children: [
      {
        path: 'chat',
        canActivate: [roleGuard('Customer')],
        loadComponent: () => import('./pages/customer/chat.component').then(m => m.ChatComponent)
      },
      {
        path: 'my-tickets',
        canActivate: [roleGuard('Customer')],
        loadComponent: () => import('./pages/customer/my-tickets.component').then(m => m.MyTicketsComponent)
      },
      {
        path: 'agent',
        canActivate: [roleGuard('Agent', 'Admin')],
        loadComponent: () => import('./pages/agent/agent-console.component').then(m => m.AgentConsoleComponent)
      },
      {
        path: 'tickets',
        canActivate: [roleGuard('Agent', 'Admin')],
        loadComponent: () => import('./pages/agent/tickets.component').then(m => m.TicketsComponent)
      },
      {
        path: 'knowledge',
        canActivate: [roleGuard('Agent', 'Admin')],
        loadComponent: () => import('./pages/admin/knowledge.component').then(m => m.KnowledgeComponent)
      },
      {
        path: 'analytics',
        canActivate: [roleGuard('Agent', 'Admin')],
        loadComponent: () => import('./pages/admin/analytics.component').then(m => m.AnalyticsComponent)
      },
      { path: '', pathMatch: 'full', redirectTo: 'chat' }
    ]
  },
  { path: '**', redirectTo: '' }
];
