import os
import json
import subprocess

cwd = r'c:\Users\bhadk\Documents\APS\aps-ui'

# 1. Install Tailwind v3 (Shadcn requires it)
print("Installing Tailwind v3...")
subprocess.run("npm install -D tailwindcss@3 postcss autoprefixer", cwd=cwd, shell=True)
subprocess.run("npx tailwindcss init -p", cwd=cwd, shell=True)

# 2. Patch tsconfig.app.json for Path Aliases
print("Patching tsconfig.app.json...")
tsConfigPath = os.path.join(cwd, 'tsconfig.app.json')
with open(tsConfigPath, 'r', encoding='utf-8') as f:
    ts_config = json.load(f)

if 'compilerOptions' not in ts_config:
    ts_config['compilerOptions'] = {}

ts_config['compilerOptions']['baseUrl'] = "."
ts_config['compilerOptions']['paths'] = {
    "@/*": [
        "./src/*"
    ]
}

with open(tsConfigPath, 'w', encoding='utf-8') as f:
    json.dump(ts_config, f, indent=2)

# 3. Patch vite.config.ts
import re
viteConfigPath = os.path.join(cwd, 'vite.config.ts')
with open(viteConfigPath, 'r', encoding='utf-8') as f:
    vite_cfg = f.read()

if "path" not in vite_cfg:
    vite_cfg = vite_cfg.replace("import { defineConfig }", "import path from 'path'\nimport { defineConfig }")
    vite_cfg = vite_cfg.replace("plugins: [react()],", "plugins: [react()],\n  resolve: {\n    alias: {\n      '@': path.resolve(__dirname, './src'),\n    },\n  },")
    with open(viteConfigPath, 'w', encoding='utf-8') as f:
        f.write(vite_cfg)

print("Setup aliases done.")

# 4. Install node types for Vite path
subprocess.run("npm i -D @types/node", cwd=cwd, shell=True)
