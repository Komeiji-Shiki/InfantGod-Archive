"""生成可辨认的已探索场景标题，并同步网页资料与原生目录快照。"""
import collections
import json
import re
from pathlib import Path
from curation import NODE_LABELS, EXPLORED_NODE_LABELS


def short_text(text):
    text = re.sub(r'<<.*?>>|#[^\s#]+|\[/?[^\]]+\]', '', text)
    text = re.sub(r'^(?:->|=>)\s*', '', text.strip())
    text = re.sub(r'^[\w·]+[:：]\s*', '', text)
    text = re.sub(r'\s+', ' ', text).strip(' …。')
    return text if len(text) <= 34 else text[:33].rstrip('，、；') + '…'


def assign_explored_titles(nodes, names):
    files = collections.defaultdict(list)
    for node in nodes:
        files[node['file']].append(node)
    for file, chapter_nodes in files.items():
        chapter = next((NODE_LABELS[n['label']].split('：')[-1] for n in chapter_nodes
                        if n['title'].startswith('Entry_') and n['label'] in NODE_LABELS), '')
        if not chapter:
            number = re.search(r'_(\d+)(?:_|\.|$)', Path(file).stem)
            chapter = '第 ' + number[1] + ' 段剧情' if number else '当前章节'
        for index, node in enumerate(chapter_nodes, 1):
            curated = EXPLORED_NODE_LABELS.get(node['label']) or NODE_LABELS.get(node['label']) or NODE_LABELS.get(node['title'])
            name = names.get(node['group'], node['group'])
            title = curated
            if not title:
                # 共同开场在任何选择与条件分支之前，不能拿完整模式的选项或后果作标题。
                lines = [] if node['opening'].startswith('此场景已经到达。') else node['opening'].splitlines()
                lines = [short_text(line) for line in lines if not re.search(r'[{}$]', line)]
                line = next((line for line in lines if len(line) >= 6), next((line for line in lines if line), ''))
                if line:
                    title = name + '：「' + line + '」'
            if not title:
                # 只有所有外部入口都对应同一个选择，才能用该选择概括已到达的片段。
                incoming = [edge for edge in node.get('incoming', []) if edge['id'] != node['id']]
                choices = {short_text(edge['path'][-1]) for edge in incoming if edge.get('path')}
                if incoming and all(edge.get('path') for edge in incoming) and len(choices) == 1:
                    title = name + '：选择「' + next(iter(choices)) + '」'
            if not title and re.search(r'[\u4e00-\u9fff]', node.get('subtitle', '')):
                title = name + '：' + short_text(node['subtitle'])
            if not title:
                kind = '话题选择' if node['options'] else '剧情片段'
                title = '{}：{} · {} {:02d}'.format(name, chapter, kind, index)
            node['exploredTitle'] = title
        counts = collections.Counter(node['exploredTitle'] for node in chapter_nodes)
        for index, node in enumerate(chapter_nodes, 1):
            if counts[node['exploredTitle']] > 1:
                node['exploredTitle'] += ' · 片段 {:02d}'.format(index)


if __name__ == '__main__':
    root = Path(__file__).resolve().parents[1]
    archive_path = root / 'data/archive.json'
    catalog_path = root / 'data/catalog.json'
    archive = json.loads(archive_path.read_text(encoding='utf-8'))
    catalog = json.loads(catalog_path.read_text(encoding='utf-8'))
    assign_explored_titles(archive['nodes'], archive['names'])
    by_id = {node['id']: node for node in archive['nodes']}
    personas = {'Empathy': '共情', 'Tech': '技术', 'Troll': '键政', 'Kitsch': '媚俗'}
    for entry in catalog['entries']:
        if entry['id'] in by_id:
            entry['exploredTitle'] = by_id[entry['id']]['exploredTitle']
        elif entry['id'].startswith('voice-'):
            node_id, persona = entry['id'][6:].rsplit('-', 1)
            entry['exploredTitle'] = personas[persona] + '人格 / ' + by_id[node_id]['exploredTitle']
    for path, data in ((archive_path, archive), (catalog_path, catalog)):
        formatting = {'indent': 2} if path == catalog_path else {'separators': (',', ':')}
        path.write_text(json.dumps(data, ensure_ascii=False, **formatting), encoding='utf-8')
    print('已同步 {} 个场景的已探索标题及人格目录。'.format(len(by_id)))
