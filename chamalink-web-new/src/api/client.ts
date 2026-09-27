import axios, { type InternalAxiosRequestConfig } from 'axios';

// BUG FIX: this was hardcoded to http://localhost:5000/api, but the
// backend actually runs on port 5168 (see ChamaLink.API launchSettings).
// Now reads from an environment variable so it works on any developer's
// machine (or a real deployment) without editing source - see .env.example.
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5168/api';

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
});

apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = localStorage.getItem('token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// If the backend ever rejects the token (expired, revoked), send the
// person back to the login screen instead of the app silently failing
// every request from then on.
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem('token');
      localStorage.removeItem('user');
      if (window.location.pathname !== '/login') {
        window.location.href = '/login';
      }
    }
    return Promise.reject(error);
  }
);
