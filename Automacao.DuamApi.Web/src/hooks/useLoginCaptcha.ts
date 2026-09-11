import { useQuery } from "@tanstack/react-query";
import { getLoginCaptcha } from "../api/duamApi";

export function useLoginCaptcha() {
  return useQuery({
    queryKey: ["login-captcha"],
    queryFn: getLoginCaptcha,
    staleTime: 0,
    gcTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
  });
}
