import { api } from './client';

export interface LoginRequest {
  username: string;
  password: string;
}

export interface UserInfo {
  displayName: string;
  username: string;
  role: string;
}

export const authApi = {
  login: (data: LoginRequest) => api.postJSON<UserInfo>('/api/auth/login', data),

  logout: () => api.postJSON<void>('/api/auth/logout'),

  getCurrentUser: () =>
    api.getJSON<UserInfo>('/api/auth/current-user', {
      expectedStatusCodes: [401]
    })
};
