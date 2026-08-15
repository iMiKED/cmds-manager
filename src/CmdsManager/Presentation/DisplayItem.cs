namespace CmdsManager.Presentation
{
    internal sealed class DisplayItem<T>
    {
        internal DisplayItem(T value, string text)
        {
            Value = value;
            Text = text;
        }

        internal T Value { get; }
        internal string Text { get; }

        public override string ToString()
        {
            return Text;
        }
    }
}
