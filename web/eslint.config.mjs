import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

export default defineConfig([
  ...nextVitals,
  ...nextTs,
  {
    rules: {
      "no-var": "error",
      "prefer-const": "error",
      "@typescript-eslint/no-explicit-any": "error",
      "@typescript-eslint/consistent-type-imports": "error",
      // Every declared function states its return type; inline callbacks take theirs from the call they are passed to.
      "@typescript-eslint/explicit-function-return-type": ["error", { allowExpressions: true, allowTypedFunctionExpressions: true }],
      "@typescript-eslint/explicit-module-boundary-types": "error",
      // Variables, parameters and members carry their type; destructured hook results keep the hook's generic.
      "@typescript-eslint/typedef": ["error", {
        variableDeclaration: true,
        variableDeclarationIgnoreFunction: true,
        parameter: true,
        memberVariableDeclaration: true,
        propertyDeclaration: true,
        arrowParameter: false,
        arrayDestructuring: false,
        objectDestructuring: false,
      }],
    },
  },
  globalIgnores([".next/**", "out/**", "next-env.d.ts"]),
]);
