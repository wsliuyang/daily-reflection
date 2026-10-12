using System;

namespace DailyReflection
{
    internal sealed class PoemQuote
    {
        internal readonly string Text;
        internal readonly string Author;
        internal readonly string Work;

        internal PoemQuote(string text, string author, string work)
        {
            Text = text; Author = author; Work = work;
        }
    }

    internal static class PoemCatalog
    {
        internal const int MaoPercent = 60;
        private static readonly Random random = new Random(Guid.NewGuid().GetHashCode());

        // Choose the author group first, so adding quotes does not change its probability.
        private static readonly PoemQuote[] mao = {
            new PoemQuote("风雨送春归，飞雪迎春到。", "毛泽东", "卜算子·咏梅"),
            new PoemQuote("江山如此多娇。", "毛泽东", "沁园春·雪"),
            new PoemQuote("鹰击长空，鱼翔浅底。", "毛泽东", "沁园春·长沙"),
            new PoemQuote("踏遍青山人未老，风景这边独好。", "毛泽东", "清平乐·会昌"),
            new PoemQuote("雄关漫道真如铁，而今迈步从头越。", "毛泽东", "忆秦娥·娄山关"),
            new PoemQuote("不管风吹浪打，胜似闲庭信步。", "毛泽东", "水调歌头·游泳"),
            new PoemQuote("一年一度秋风劲，不似春光。", "毛泽东", "采桑子·重阳"),
            new PoemQuote("赤橙黄绿青蓝紫，谁持彩练当空舞？", "毛泽东", "菩萨蛮·大柏地"),
            new PoemQuote("天若有情天亦老，人间正道是沧桑。", "毛泽东", "七律·人民解放军占领南京"),
            new PoemQuote("红雨随心翻作浪，青山着意化为桥。", "毛泽东", "七律·送瘟神·其二"),
            new PoemQuote("一万年太久，只争朝夕。", "毛泽东", "满江红·和郭沫若同志"),
            new PoemQuote("久有凌云志，重上井冈山。", "毛泽东", "水调歌头·重上井冈山")
        };

        private static readonly PoemQuote[] others = {
            new PoemQuote("举头望明月，低头思故乡。", "李白", "静夜思"),
            new PoemQuote("随风潜入夜，润物细无声。", "杜甫", "春夜喜雨"),
            new PoemQuote("明月松间照，清泉石上流。", "王维", "山居秋暝"),
            new PoemQuote("人有悲欢离合，月有阴晴圆缺。", "苏轼", "水调歌头·明月几时有"),
            new PoemQuote("但愿人长久，千里共婵娟。", "苏轼", "水调歌头·明月几时有"),
            new PoemQuote("采菊东篱下，悠然见南山。", "陶渊明", "饮酒·其五"),
            new PoemQuote("绿树村边合，青山郭外斜。", "孟浩然", "过故人庄"),
            new PoemQuote("一道残阳铺水中，半江瑟瑟半江红。", "白居易", "暮江吟"),
            new PoemQuote("晴空一鹤排云上，便引诗情到碧霄。", "刘禹锡", "秋词·其一"),
            new PoemQuote("稻花香里说丰年，听取蛙声一片。", "辛弃疾", "西江月·夜行黄沙道中"),
            new PoemQuote("山重水复疑无路，柳暗花明又一村。", "陆游", "游山西村"),
            new PoemQuote("停车坐爱枫林晚，霜叶红于二月花。", "杜牧", "山行"),
            new PoemQuote("春风又绿江南岸，明月何时照我还？", "王安石", "泊船瓜洲"),
            new PoemQuote("海上生明月，天涯共此时。", "张九龄", "望月怀远"),
            new PoemQuote("争渡，争渡，惊起一滩鸥鹭。", "李清照", "如梦令·常记溪亭日暮"),
            new PoemQuote("何当共剪西窗烛，却话巴山夜雨时。", "李商隐", "夜雨寄北"),
            new PoemQuote("轻轻的我走了，正如我轻轻的来。", "徐志摩", "再别康桥"),
            new PoemQuote("等闲识得东风面，万紫千红总是春。", "朱熹", "春日")
        };

        internal static PoemQuote Pick()
        {
            lock (random) { return Pick(random); }
        }

        internal static PoemQuote Pick(Random source)
        {
            if (source == null) throw new ArgumentNullException("source");
            PoemQuote[] group = source.Next(100) < MaoPercent ? mao : others;
            return group[source.Next(group.Length)];
        }
    }
}
