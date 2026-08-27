import { useEffect } from "react";
import { driver, type DriveStep } from "driver.js";
import { hasSeenTour, markTourSeen } from "../lib/tourStorage";

/**
 * Roda um tour guiado (driver.js) automaticamente na primeira vez que a
 * página monta, uma única vez por `key` (guardado em localStorage).
 * `steps` é lido só na primeira execução do efeito — recriar o array a
 * cada render (comum quando é definido inline no componente) não reabre
 * o tour.
 *
 * `enabled` deixa o disparo condicionado a algo além do mount (ex.: só
 * depois que os elementos-alvo existirem no DOM, quando dependem de dados
 * carregados de forma assíncrona).
 */
export function useAutoTour(key: string, steps: DriveStep[], enabled = true) {
  useEffect(() => {
    if (!enabled) return;
    if (hasSeenTour(key)) return;

    // pequeno atraso pra garantir que os elementos-alvo já estão no DOM
    const timer = setTimeout(() => {
      const tour = driver({
        showProgress: true,
        nextBtnText: "Próximo",
        prevBtnText: "Voltar",
        doneBtnText: "Concluir",
        progressText: "{{current}} de {{total}}",
        steps,
        onDestroyed: () => markTourSeen(key),
      });

      tour.drive();
    }, 300);

    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, enabled]);
}
