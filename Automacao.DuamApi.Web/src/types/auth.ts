export interface LoginResponse {
  login: string;
  nomeEmpresa: string | null;
}

export interface CaptchaChallenge {
  captchaNecessario: boolean;
  token: string | null;
  imagemBase64: string | null;
}
