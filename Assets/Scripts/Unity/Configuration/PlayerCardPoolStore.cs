using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MagicBrawl.Core;
using UnityEngine;

namespace MagicBrawl.App
{
    /// <summary>测试用本地覆盖；只存稳定卡 ID，不修改角色资产或序列化运行中的牌。</summary>
    public sealed class PlayerCardPoolStore
    {
        [Serializable] private sealed class Document
        {
            public int version = 1;
            public List<Entry> characters = new List<Entry>();
        }
        [Serializable] private sealed class Entry
        {
            public string characterId;
            public List<string> cardIds;
        }

        public string FilePath { get; private set; }
        public PlayerCardPoolStore(string path = null)
        {
            FilePath = path ?? Path.Combine(Application.persistentDataPath, "test-card-pools.json");
        }

        private Document Read()
        {
            if (!File.Exists(FilePath)) return new Document();
            Document data = JsonUtility.FromJson<Document>(File.ReadAllText(FilePath, Encoding.UTF8));
            if (data == null || data.version != 1 || data.characters == null)
                throw new InvalidDataException("卡池存档格式或版本不受支持。");
            var ids = new HashSet<string>();
            foreach (Entry entry in data.characters)
                if (entry == null || string.IsNullOrWhiteSpace(entry.characterId) || entry.cardIds == null || !ids.Add(entry.characterId))
                    throw new InvalidDataException("卡池存档中的角色记录无效或重复。");
            return data;
        }

        public bool TryLoad(CharacterDefinition definition, out CardPool pool, out string error)
        {
            pool = definition.CreateCardPool(); error = null;
            try
            {
                foreach (Entry entry in Read().characters)
                {
                    if (entry.characterId != definition.Id) continue;
                    var saved = new CardPool(entry.cardIds, pool.Catalog);
                    if (!saved.Validate(definition.MinimumCardCount, out error)) return false;
                    pool = saved;
                    return true;
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                error = "无法读取测试卡池：" + ex.Message;
                return false;
            }
        }

        public bool TrySave(CharacterDefinition definition, CardPool pool, out string error)
        {
            if (!pool.Validate(definition.MinimumCardCount, out error)) return false;
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Document data = Read();
                Entry entry = data.characters.Find(item => item.characterId == definition.Id);
                if (entry == null)
                {
                    entry = new Entry { characterId = definition.Id };
                    data.characters.Add(entry);
                }
                entry.cardIds = new List<string>();
                foreach (CardDef card in pool.Resolve()) entry.cardIds.Add(card.Id);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                error = "保存失败，对局未重开：" + ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
