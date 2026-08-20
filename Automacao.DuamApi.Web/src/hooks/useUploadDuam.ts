import { useMutation } from "@tanstack/react-query";
import { postProcessar, type PostProcessarInput } from "../api/duamApi";

export function useUploadDuam() {
  return useMutation({
    mutationFn: (input: PostProcessarInput) => postProcessar(input),
  });
}
