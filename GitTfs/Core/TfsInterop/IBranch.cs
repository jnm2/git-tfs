using System;
using System.Linq;
using System.Collections.Generic;
using Sep.Git.Tfs.Core.BranchVisitors;

namespace Sep.Git.Tfs.Core.TfsInterop
{
    public interface IBranchObject
    {
        string Path { get; }
        string ParentPath { get; }
        bool IsRoot { get; }
    }

    public class BranchTree
    {
        public BranchTree(IBranchObject branch)
            : this(branch, new List<BranchTree>())
        {
        }

        public BranchTree(IBranchObject branch, IEnumerable<BranchTree> childBranches)
            : this(branch, childBranches.ToList())
        {
        }

        public BranchTree(IBranchObject branch, List<BranchTree> childBranches)
        {
            if (childBranches == null)
                throw new ArgumentNullException("childBranches");
            Branch = branch;
            ChildBranches = childBranches;
        }

        public IBranchObject Branch { get; private set; }

        public List<BranchTree> ChildBranches { get; private set; }

        public string Path { get { return Branch.Path; } }
        public string ParentPath { get { return Branch.ParentPath; } }
        public bool IsRoot { get { return Branch.IsRoot; } }

        public override string ToString()
        {
            return string.Format("{0} [{1} children]", this.Path, this.ChildBranches.Count);
        }
    }

    public static class BranchExtensions
    {
        public static BranchTree GetRootTfsBranchForRemotePath(this ITfsHelper tfs, string remoteTfsPath, bool searchExactPath = true)
        {
            var branches = tfs.GetBranches().Select(branch => new BranchTree(branch)).ToList();
            var branchesByPath = branches.ToLookup(branch => branch.Path, StringComparer.OrdinalIgnoreCase);

            foreach (var branch in branches)
            {
                if (branch.IsRoot) continue;

                //in some strange cases there might be a branch which is not marked as IsRoot
                //but the parent for this branch is missing.
                var possibleParents = branchesByPath[branch.ParentPath];
                switch (possibleParents.Count())
                {
                    case 0:
                        break;
                    case 1:
                        possibleParents.Single().ChildBranches.Add(branch);
                        break;
                    default:
                        throw new GitTfsException($"Cannot uniquely identify parent branch because more than one branch had the path \"{branch.ParentPath}\".");
                }
            }

            var roots = branches.Where(b => b.IsRoot);
            return roots.FirstOrDefault(b =>
            {
                var visitor = new BranchTreeContainsPathVisitor(remoteTfsPath, searchExactPath);
                b.AcceptVisitor(visitor);
                return visitor.Found;
            });
        }

        public static void AcceptVisitor(this BranchTree branch, IBranchTreeVisitor treeVisitor, int level = 0)
        {
            treeVisitor.Visit(branch, level);
            foreach (var childBranch in branch.ChildBranches)
            {
                childBranch.AcceptVisitor(treeVisitor, level + 1);
            }
        }

        public static IEnumerable<BranchTree> GetAllChildren(this BranchTree branch)
        {
            if (branch == null) return Enumerable.Empty<BranchTree>();

            var childrenBranches = new List<BranchTree>(branch.ChildBranches);
            foreach (var childBranch in branch.ChildBranches)
            {
                childrenBranches.AddRange(childBranch.GetAllChildren());
            }
            return childrenBranches;
        }

        public static IEnumerable<BranchTree> GetAllChildrenOfBranch(this BranchTree branch, string tfsPath)
        {
            if (branch == null) return Enumerable.Empty<BranchTree>();

            if (string.Compare(branch.Path, tfsPath, StringComparison.InvariantCultureIgnoreCase) == 0)
                return branch.GetAllChildren();

            var childrenBranches = new List<BranchTree>();
            foreach (var childBranch in branch.ChildBranches)
            {
                childrenBranches.AddRange(GetAllChildrenOfBranch(childBranch, tfsPath));
            }
            return childrenBranches;
        }
    }
}