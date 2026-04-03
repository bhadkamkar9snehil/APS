import re

file_path = r'c:\Users\bhadk\Documents\APS\ui_design\index.html'

with open(file_path, 'r', encoding='utf-8') as f:
    text = f.read()

def replacer(match):
    val = float(match.group(1))
    if val == 0:
        return "0"
    rem_val = val / 16.0
    # Formatting to remove trailing zeros and capping decimal places
    rem_str = "{:.4f}".format(rem_val).rstrip('0').rstrip('.')
    return f"{rem_str}rem"

# Regex to match Xpx or X.Xpx, but we only want to do this within the <style> tag
start_style = text.find('<style>')
end_style = text.find('</style>')

if start_style != -1 and end_style != -1:
    style_content = text[start_style:end_style]
    # Replace any px value in style content to rem
    new_style = re.sub(r'\b([0-9]+(?:\.[0-9]+)?)px\b', replacer, style_content)
    # Re-assemble
    text = text[:start_style] + new_style + text[end_style:]

# Also let's inject Google Fonts into head
font_link = '<link rel="preconnect" href="https://fonts.googleapis.com">\n<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>\n<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800;900&display=swap" rel="stylesheet">\n'
text = text.replace('</title>\n', '</title>\n' + font_link)
# Change the font-family definition in CSS rule mapping
text = text.replace("font-family:'Segoe UI',system-ui,sans-serif;", "font-family:'Inter',system-ui,sans-serif;")

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(text)

print("Converted px to rem and added Inter font.")
