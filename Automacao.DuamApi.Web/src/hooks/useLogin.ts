import { useMutation } from "@tanstack/react-query";
import { postLogin, type PostLoginInput } from "../api/duamApi";
import { saveLogin } from "../lib/authStorage";

export function useLogin() {
  return useMutation({
    mutationFn: (input: PostLoginInput) => postLogin(input),
    onSuccess: (session, variables) => {
      saveLogin(variables.usuario, session);
    },
  });
}
