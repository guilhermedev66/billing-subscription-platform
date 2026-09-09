/** Joins class names, dropping falsy values. Not clsx/tailwind-merge — no conflicting variants to resolve yet. */
export function cn(...classes: Array<string | false | null | undefined>): string {
  return classes.filter(Boolean).join(' ')
}
