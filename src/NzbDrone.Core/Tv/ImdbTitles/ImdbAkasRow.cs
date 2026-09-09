namespace NzbDrone.Core.Tv.ImdbTitles
{
    // krzw(imdb-title-provider): one kept row of IMDb's title.akas dataset. Null means the dataset
    // had "\N" in that column.
    public class ImdbAkasRow
    {
        public string Tconst { get; set; }
        public string Title { get; set; }
        public string Region { get; set; }
        public string Language { get; set; }
        public string Attributes { get; set; }
        public bool IsOriginal { get; set; }
    }
}
