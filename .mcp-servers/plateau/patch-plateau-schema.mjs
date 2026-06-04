import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const target = join(
  here,
  "node_modules",
  "@yodolabs",
  "plateau-creative-mcp",
  "dist",
  "schemas",
  "zodJsonSchema.js",
);

const marker = "function normalizeDraft202012(schema)";
let source = readFileSync(target, "utf8");

if (!source.includes(marker)) {
  source = source.replace(
    'import { zodToJsonSchema as convert } from "zod-to-json-schema";\n',
    `import { zodToJsonSchema as convert } from "zod-to-json-schema";
function normalizeDraft202012(schema) {
    if (!schema || typeof schema !== "object")
        return schema;
    if (Array.isArray(schema)) {
        for (const item of schema)
            normalizeDraft202012(item);
        return schema;
    }
    if (Array.isArray(schema.items)) {
        schema.prefixItems = schema.items;
        if (schema.minItems === schema.maxItems && schema.maxItems === schema.prefixItems.length) {
            schema.items = false;
        }
        else {
            delete schema.items;
        }
    }
    for (const value of Object.values(schema))
        normalizeDraft202012(value);
    return schema;
}
`,
  );
  source = source.replace("    return result;\n", "    return normalizeDraft202012(result);\n");
  writeFileSync(target, source);
}
