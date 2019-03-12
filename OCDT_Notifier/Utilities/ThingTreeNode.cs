using NLog;
using Ocdt.DomainModel;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OCDT_Notifier.Utilities
{
    class ThingTreeNode : IEnumerable<ThingTreeNode>
    {
        /// <summary>
        /// The logger.
        /// </summary>
        protected static new readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<Guid, ThingTreeNode> _children = new Dictionary<Guid, ThingTreeNode>();
        public ThingTreeNode Parent { get; private set; }

        public Thing thing;

        public ThingTreeNode(Thing thing)
        {
            this.thing = thing;
        }

        public ThingTreeNode GetChild(Guid guid)
        {
            return this._children[guid];
        }

        /// <summary>
        /// Perform a depth-first traversal of the thing
        /// </summary>
        /// <param name="func">The function to apply to every Thing. Takes one argument, which is the Thing.</param>
        public void Traverse(Action<Thing> func)
        {
            foreach (ThingTreeNode thingTreeNode in _children.Values) {
                Logger.Trace("Tree traversal: {}", thingTreeNode.thing.ToShortString());

                // Call the provided function
                func(thingTreeNode.thing);

                // Recursively traverse the children as well
                thingTreeNode.Traverse(func);
            }
        }

        public void Add(Thing thing)
        {
            if (this._children.ContainsKey(thing.Iid)) {
                // This child already exists. No need to add anything
                return;
            }

            Add(new ThingTreeNode(thing));
        }

        public void Add(ThingTreeNode item)
        {
            if (item.Parent != null) item.Parent._children.Remove(item.thing.Iid);

            item.Parent = this;
            this._children.Add(item.thing.Iid, item);
        }

        public IEnumerator<ThingTreeNode> GetEnumerator()
        {
            return this._children.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public int Count
        {
            get { return this._children.Count; }
        }

        public void AddThingAndItsContainers(Thing thing)
        {
            // The container hierarchy of the thing
            List<Thing> thingsBottomToTop = new List<Thing>();

            var currentContainer = thing;
            // Find the container hierarchy of the thing
            for (int depth = 0; depth < 10; depth++) { // Make sure we don't go too deep
                if (currentContainer == null) break;

                thingsBottomToTop.Add(currentContainer);

                if (currentContainer.ClassKind.IsOneOf(new ClassKind[] {
                    ClassKind.ElementDefinition,
                    ClassKind.ElementUsage
                })) {
                    // We assume that ElementDefinitions and ElementUsages are at the top
                    // TODO: Add Owner as parent?
                    break;
                }

                currentContainer = currentContainer.Container;
            }

            // Starting from top to bottom, add the Things to the tree
            thingsBottomToTop.Reverse();
            ThingTreeNode currentNode = this;
            foreach (Thing container in thingsBottomToTop) {
                currentNode.Add(container);
                currentNode = currentNode.GetChild(container.Iid);
            }
        }
    }
}
