#!/usr/bin/env python3
"""Render data-driven SVG/LaTeX figures, verify tables, and sync Markdown."""
import argparse
import html
import json
import math
from pathlib import Path
import re


def group(text, start):
    if text[start] != "{": raise ValueError("Expected brace")
    depth = 1; i = start + 1
    while depth:
        if text[i] == "\\": i += 2; continue
        if text[i] == "{": depth += 1
        if text[i] == "}": depth -= 1
        i += 1
    return text[start+1:i-1], i


def tex_escape(text):
    return str(text).replace("&", r"\&").replace("%", r"\%").replace("_", r"\_")


class Canvas:
    def __init__(self, width=195, height=120):
        self.w, self.h = width, height
        self.svg = []; self.tex = []
    def color(self, color): return r"\color[HTML]{" + color.lstrip("#") + "}"
    def line(self, x1, y1, x2, y2, color="#d8d8d8"):
        self.svg.append(f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="0.15"/>')
        if abs(y2-y1) < .001: direction, length, x, y = "1,0", abs(x2-x1), min(x1,x2), y1
        elif abs(x2-x1) < .001: direction, length, x, y = "0,1", abs(y2-y1), x1, max(y1,y2)
        else: return
        self.tex.append(r"\put(%.3f,%.3f){%s\linethickness{0.15mm}\line(%s){%.3f}}" % (x,self.h-y,self.color(color),direction,length))
    def rect(self, x, y, width, height, color):
        self.svg.append(f'<rect x="{x}" y="{y}" width="{max(width,.01)}" height="{height}" fill="{color}"/>')
        self.tex.append(r"\put(%.3f,%.3f){%s\rule{%.4fmm}{%.3fmm}}" % (x,self.h-y-height,self.color(color),max(width,.01),height))
    def text(self, x, y, value, anchor="start", size=9):
        self.svg.append(f'<text x="{x}" y="{y}" text-anchor="{anchor}" font-size="{size*.352778:.3f}">{html.escape(str(value))}</text>')
        align = {"start":"l","end":"r","middle":"c"}[anchor]
        self.tex.append(r"\put(%.3f,%.3f){\makebox(0,0)[%s]{\fontsize{%s}{11}\selectfont %s}}" % (x,self.h-y,align,size,tex_escape(value)))
    def marker(self,x,y,color,kind):
        if kind == "square": return self.rect(x-.7,y-.7,1.4,1.4,color)
        if kind == "triangle":
            self.svg.append(f'<polygon points="{x},{y-.9} {x-.8},{y+.7} {x+.8},{y+.7}" fill="{color}"/>')
            self.tex.append(r"\put(%.3f,%.3f){%s\makebox(0,0){$\triangle$}}" % (x,self.h-y,self.color(color)))
        else:
            self.svg.append(f'<circle cx="{x}" cy="{y}" r=".75" fill="{color}"/>')
            self.tex.append(r"\put(%.3f,%.3f){%s\circle*{1.5}}" % (x,self.h-y,self.color(color)))
    def save(self,path):
        path.with_suffix(".svg").write_text(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {self.w} {self.h}" width="{self.w}mm" height="{self.h}mm"><rect width="100%" height="100%" fill="white"/><g font-family="DejaVu Sans,Arial,sans-serif" fill="#222">'+"\n".join(self.svg)+"</g></svg>\n",encoding="utf-8")
        preamble = r"""\documentclass[10pt]{article}
\usepackage[utf8]{inputenc}
\usepackage[T2A]{fontenc}
\usepackage[russian]{babel}
\usepackage{xcolor}
\usepackage[paperwidth=195mm,paperheight=120mm,margin=0mm]{geometry}
\pagestyle{empty}
\setlength{\parindent}{0pt}
\setlength{\unitlength}{1mm}
\begin{document}
\noindent\begin{picture}(195,119.9)
"""
        path.with_suffix(".tex").write_text(preamble+"\n".join(self.tex)+"\n\\end{picture}\n\\end{document}\n",encoding="utf-8")


def plots(data, all_sizes, directory):
    rows = data["rows"]; blue, orange, green = "#267CC0", "#E56C2F", "#15966A"
    c = Canvas()
    for i,(label,color) in enumerate((("подготовка",blue),("счёт",orange),("заключительный этап",green))):
        x = (67,102,128)[i]; c.rect(x,5,3,2,color); c.text(x+4,7.1,label,size=8)
    x0,scale = 67,1.08
    for tick in range(0,101,20):
        x=x0+tick*scale; c.line(x,14,x,103); c.text(x,108,str(tick),"middle",8)
    for i,row in enumerate(rows):
        y=18+i*7.5; c.text(x0-2,y+2,row["name"],"end",8)
        x=x0
        for value,color in ((row["besm_preparation_s"],blue),(row["besm_compute_s"],orange),(row["besm_io_s"],green)):
            c.rect(x,y,value*scale,3,color); x+=value*scale
        c.text(x+1.5,y+2,f'{row["besm_compute_fraction"]*100:.2f} %'.replace('.',','),size=7.5)
    c.line(x0,103,184,103,"#666666"); c.text(125,116,"Модельное время задания БЭСМ-6, с","middle",9)
    c.save(directory/"fig-composition-linux")
    c=Canvas(); x0=67
    def xpos(seconds): return x0+(math.log10(seconds)+4)*18.5
    for exponent,label in [(-4,"0,1 мс"),(-3,"1 мс"),(-2,"10 мс"),(-1,"0,1 с"),(0,"1 с"),(1,"10 с"),(2,"100 с")]:
        x=xpos(10**exponent); c.line(x,13,x,102);c.text(x,107,label,"middle",8)
    for i,row in enumerate(rows):
        y=18+i*7.5;c.text(x0-2,y+1,row["name"],"end",8)
        values=[(row["besm_compute_s"],blue,"circle"),(row["python_solve_s"],orange,"square"),(row["fortran_solve_s"],green,"triangle")]
        c.line(min(xpos(v) for v,_,_ in values),y,max(xpos(v) for v,_,_ in values),y)
        for value,color,kind in values:c.marker(xpos(value),y,color,kind)
    for x,label,color,kind in [(48,"БЭСМ-6: модель",blue,"circle"),(101,"CPython: измерение",orange,"square"),(153,"GNU Fortran -O0",green,"triangle")]:
        c.marker(x-3,115,color,kind);c.text(x,116,label,size=8)
    c.save(directory/"fig-compute-linux")
    c=Canvas(); c.rect(67,5,3,2,blue);c.text(71,7,"малый вариант",size=8);c.rect(121,5,3,2,orange);c.text(125,7,"увеличенный вариант",size=8)
    for tick in range(0,101,20):
        x=67+tick*1.08;c.line(x,14,x,103);c.text(x,108,str(tick),"middle",8)
    for i,row in enumerate(r for r in rows if r["case_id"]!="mlp-l0"):
        algorithm=row["case_id"].rsplit("-l",1)[0]
        small=next(r for r in all_sizes if r["algorithm"]==algorithm and r["level"]==0)
        y=18+i*8.2;c.text(65,y+2,row["name"],"end",8)
        c.rect(67,y,small["computeFraction"]*108,1.5,blue)
        c.rect(67,y+2,row["besm_compute_fraction"]*108,1.5,orange)
        c.text(68+row["besm_compute_fraction"]*108,y+3.4,f'{row["besm_compute_fraction"]*100:.1f}%'.replace('.',','),size=7.5)
    c.text(123,116,"Доля счёта в полном модельном времени, %","middle",9)
    c.save(directory/"fig-scaling-linux")


def split_outside(text, separator):
    result=[]; start=0;depth=0;i=0
    while i<len(text):
        if depth==0 and text.startswith(separator,i):result.append(text[start:i]);i+=len(separator);start=i;continue
        if text[i]=="{":depth+=1
        if text[i]=="}":depth-=1
        i+=1
    result.append(text[start:]);return result


def table_rows(block):
    match=re.search(r"\\begin\{tabular\}\{[^\n]*\}",block)
    body=block[match.end():block.index(r"\end{tabular}")]
    body=re.sub(r"\\hline|\\cline\{[^}]*\}","",body)
    result=[]
    for line in split_outside(body,r"\\"):
        cells=[cell.strip() for cell in split_outside(line,"&")]
        if len(cells)>1 and re.fullmatch(r"[\d~, .]+",cells[1]):result.append(cells)
        elif len(cells)>1 and cells[0].isdigit():result.append(cells)
    return result


def markdown(tex,data,destination):
    bibliography=re.findall(r"\\bibitem\{([^}]+)\}\s*([^\n]+)",tex)
    citations={key:(i%18)+1 for i,(key,_) in enumerate(bibliography)}
    blocks=[]; inline=[]; labels={}
    def keep(value):blocks.append(value);return f"@@BLOCK{len(blocks)-1}@@"
    def unwrap(text,name,fn):
        pattern=re.compile(r"\\"+name+r"\{")
        while match:=pattern.search(text):
            content,end=group(text,match.end()-1)
            text=text[:match.start()]+fn(content)+text[end:]
        return text
    def plain(text):
        text=re.sub(r"\\allowbreak(?:\{\})?\s*","",text)
        text=unwrap(text,"texttt",lambda value:"`"+value.replace(r"\_","_").replace("~"," ")+"`")
        text=unwrap(text,"textbf",lambda value:"**"+plain(value)+"**")
        text=unwrap(text,"textit",lambda value:"*"+plain(value)+"*")
        for command in ("mbox","hl","hd"):text=unwrap(text,command,lambda value:plain(value).replace(r"\\","<br>"))
        text=re.sub(r"\\cite\{([^}]+)\}",lambda m:"["+", ".join(str(citations[k]) for k in m[1].split(","))+"]",text)
        text=re.sub(r"\\ref\{([^}]+)\}",lambda m:str(labels.get(m[1],"?")),text)
        text=re.sub(r"\{\\it\s+([^{}]+)\}",r"*\1*",text)
        text=re.sub(r"\\(?:protect|rmfamily|noindent|smallskip|frenchspacing|small|allowbreak)\b","",text)
        text=text.replace(r"\%","%").replace(r"\#","#").replace(r"\_","_").replace(r"\,"," ").replace(r"\ "," ").replace("~"," ").replace("<<","«").replace(">>","»").replace("---","—")
        text=re.sub(r"(?<=\d)--(?=\d)","–",text)
        return text.replace("$--$","$–$").strip()
    main=tex[tex.index(r"\section{Введение}"):tex.index(r"{\small\frenchspacing",tex.index(r"\section{Введение}"))]
    main=re.sub(r"(?m)^\s*%[^\n]*$","",main)
    main=re.sub(r"(?s)\\begin\{verbatim\}\n(.*?)\\end\{verbatim\}",lambda m:keep("```bash\n"+m[1].rstrip()+"\n```"),main)
    main=re.sub(r"(?m)^\{\\small\s*$|^\}\s*$","",main)
    figures=re.findall(r"(?s)\\begin\{figure\}.*?\\end\{figure\}",main)
    for number,block in enumerate(figures,1):
        label=re.search(r"\\label\{([^}]+)\}",block)
        if label:labels[label[1]]=number
    figure_number=0
    def figure(match):
        nonlocal figure_number
        figure_number+=1
        block=match[0];caption_match=re.search(r"\\caption\{",block);caption,_=group(block,caption_match.end()-1)
        filename=re.search(r"fig-[a-z-]+\.pdf|48-bit\.png",block)[0]
        image=filename.replace(".pdf",".svg")
        return keep(f"![{plain(caption)}]({image})\n\n*Рис. {figure_number}. {plain(caption)}*")
    main=re.sub(r"(?s)\\begin\{figure\}.*?\\end\{figure\}",figure,main)
    headers=[['№','Программа','Размер задачи','Команд счёта, млн'],['Программа','Подготовка БЭСМ-6, с','Счёт БЭСМ-6, с','Заключительный этап, мс','CPython: счёт, мс','GNU Fortran: счёт, мс','Доля счёта, %'],['Программа','БЭСМ-6/CPython','БЭСМ-6/Fortran','CPython/Fortran','Процесс CPython, мс','Процесс Fortran, мс','Компиляция Fortran, мс']]
    tables=re.findall(r"(?s)\\begin\{table\}.*?\\end\{table\}",main)
    for i,block in enumerate(tables):
        rows=table_rows(block)
        if len(rows)!=11:raise ValueError(f"Table {i+1}: expected 11 rows, got {len(rows)}")
        caption=re.search(r"\\textbf\{Таблица~\d+\}(.*?)\\par",block,re.S)
        if caption is None:raise ValueError(f"Table {i+1}: caption not found")
        rendered=f"**Таблица {i+1}. {plain(caption[1])}**\n\n| "+" | ".join(headers[i])+" |\n|"+"---|"*len(headers[i])+"\n"
        rendered+="\n".join("| "+" | ".join(plain(cell) for cell in row)+" |" for row in rows)
        note=re.search(r"\\footnotesize\s+(.*?)\}\s*\\end\{table\}",block,re.S)
        if note:rendered+="\n\n"+plain(note[1])
        main=main.replace(block,keep(rendered))
    section=0;subsection=0
    def heading(match):
        nonlocal section,subsection
        command,star,value=match.groups();value=re.sub(r"[ \t]+"," ",plain(value))
        if command=="section":
            if not star:section+=1;subsection=0;value=str(section)+". "+value
            return "\n## "+value+"\n"
        if not star:subsection+=1;value=f"{section}.{subsection}. "+value
        return "\n### "+value+"\n"
    main=re.sub(r"\\(section|subsection)(\*?)\{([^}]+)\}",heading,main)
    main=re.sub(r"\\label\{[^}]*\}","",main)
    main=re.sub(r"\\begin\{itemize\}[^\n]*|\\end\{itemize\}","",main)
    main=main.replace(r"\item ","- ")
    main=plain(main)
    main=re.sub(r"(?m)^ +(?=\S)","",main)
    for i,value in enumerate(blocks):main=main.replace(f"@@BLOCK{i}@@",value)
    abstract_match=re.search(r"\\Abst\{",tex);abstract,_=group(tex,abstract_match.end()-1)
    english_match=re.search(r"\\Abste\{",tex);english,_=group(tex,english_match.end()-1)
    titles=[plain(group(tex,match.end()-1)[0]) for match in re.finditer(r"\\def\\tit\{",tex)]
    if len(titles)!=2:raise ValueError("Expected Russian and English article titles")
    title,english_title=titles
    keywords_match=re.search(r"\\KW\{",tex);keywords,_=group(tex,keywords_match.end()-1)
    english_keywords_match=re.search(r"\\KWE\{",tex);english_keywords,_=group(tex,english_keywords_match.end()-1)
    result="# "+title+"\n\n**А. В. Солопий, Р. Ф. Гибадуллин**\n\nКазанский национальный исследовательский технический университет им. А. Н. Туполева — КАИ, Казань, Россия\n\n## Аннотация\n\n"+plain(abstract)+"\n\n**Ключевые слова:** "+plain(keywords)+"\n\n"+main+"\n\n## Литература\n\n"
    result+="\n".join(f"{i+1}. {plain(entry)}" for i,(_,entry) in enumerate(bibliography[:18]))
    result+="\n\n# "+english_title+"\n\n## Abstract\n\n"+plain(english)+"\n\n**Keywords:** "+plain(english_keywords)+"\n\n## References\n\n"
    result+="\n".join(f"{i+1}. {plain(entry)}" for i,(_,entry) in enumerate(bibliography[18:]))
    destination.write_text(re.sub(r"\n{3,}","\n\n",result)+"\n",encoding="utf-8")


def verify_tables(tex,data):
    tables=re.findall(r"(?s)\\begin\{table\}.*?\\end\{table\}",tex)
    if len(tables)!=3:raise ValueError("Expected three current-series tables")
    def numeric(text):return text.replace("~","").replace(" ","").replace(",",".")
    failures=[]
    for number,block in enumerate(tables):
        rows=table_rows(block)
        if len(rows)!=11:raise ValueError(f"Table {number+1}: row count {len(rows)}")
        for row,datum in zip(rows,data["rows"]):
            if number==0:expected=[f'{datum["besm_instructions_per_pass"]/1e6:.3f}'];actual=row[-1:]
            elif number==1:
                expected=[f'{datum["besm_preparation_s"]:.3f}',f'{datum["besm_compute_s"]:.3f}',f'{datum["besm_io_s"]*1000:.3f}',f'{datum["python_solve_s"]*1000:.3f}',f'{datum["fortran_solve_s"]*1000:.3f}',f'{datum["besm_compute_fraction"]*100:.2f}'];actual=row[1:]
            else:
                expected=[f'{datum["speedup_besm_python_solve"]:.0f}',f'{datum["speedup_besm_fortran_solve"]:.0f}',f'{datum["speedup_fortran_python_solve"]:.1f}',f'{datum["python_process_s"]*1000:.3f}',f'{datum["fortran_process_s"]*1000:.3f}',f'{datum["fortran_compile_s"]*1000:.3f}'];actual=row[1:]
            if list(map(numeric,actual))!=expected:failures.append((number+1,datum['id'],actual,expected))
    if failures:raise ValueError(failures)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--tex',type=Path,required=True)
    parser.add_argument('--data',type=Path,required=True)
    parser.add_argument('--all-sizes',type=Path,required=True)
    parser.add_argument('--figures',type=Path,required=True)
    parser.add_argument('--markdown',type=Path,required=True)
    args=parser.parse_args()
    data=json.loads(args.data.read_text(encoding='utf-8-sig'));tex=args.tex.read_text(encoding='utf-8')
    verify_tables(tex,data)
    args.figures.mkdir(parents=True,exist_ok=True)
    plots(data,json.loads(args.all_sizes.read_text(encoding='utf-8')),args.figures)
    markdown(tex,data,args.markdown)
    print('Verified 143 table cells; rendered three SVG/LaTeX figures and synchronized Markdown.')


if __name__=='__main__':main()
